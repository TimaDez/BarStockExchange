using System.Net;
using System.Net.Http.Json; // FIX
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OrdersApi", Version = "v1" }); // FIX (title)

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<OrdersDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Db");
    options.UseNpgsql(cs);
});

// JWT validation (כמו Auth) — כל שירות צריך לאמת טוקן בעצמו
var issuer = builder.Configuration["Jwt:Issuer"]!;
var audience = builder.Configuration["Jwt:Audience"]!;
var key = builder.Configuration["Jwt:Key"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.FromSeconds(15)
        };
    });

var inventoryBaseUrl = builder.Configuration["Services:InventoryBaseUrl"] ?? "http://inventoryapi:8080";
builder.Services.AddHttpClient("Inventory", client => { client.BaseAddress = new Uri(inventoryBaseUrl); });

builder.Services.AddAuthorization();

// RabbitMQ + Outbox
var rabbitOptions = new RabbitMqOptions();
builder.Configuration.GetSection("RabbitMq").Bind(rabbitOptions);
builder.Services.AddSingleton(rabbitOptions);
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Text("Orders API is running", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.MigrateAsync();
}

// Helpers
static bool TryGetPubId(ClaimsPrincipal user, out Guid pubId)
{
    var pubIdStr = user.FindFirstValue("pub_id");
    return Guid.TryParse(pubIdStr, out pubId);
}

static string? GetRole(ClaimsPrincipal user) => user.FindFirstValue("role");

// POST /api/orders (לקוח)
app.MapPost("/api/orders", [Authorize] async (
        CreateOrderRequest req,
        ClaimsPrincipal user,
        OrdersDbContext db,
        IHttpClientFactory httpClientFactory,
        HttpContext httpContext
    ) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        if (req.Items is null || req.Items.Count == 0)
            return Results.BadRequest(new { error = "Order must contain at least one item." });

        foreach (var item in req.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku) || item.Quantity <= 0)
                return Results.BadRequest(new { error = "Invalid item in order." });
        }

        // Build reserve lines outside try so we can use later // FIX
        var reserveLines = req.Items
            .GroupBy(i => i.Sku.Trim().ToUpperInvariant())
            .Select(g => new InventoryReserveLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        InventoryReserveResponse? reserveRespDto = null;

        try
        {
            var inventoryClient = httpClientFactory.CreateClient("Inventory");
            var reserveReq = new InventoryReserveRequest(reserveLines);

            // Forward user's bearer token to InventoryApi (Inventory is [Authorize])
            var authHeader = httpContext.Request.Headers.Authorization.ToString();

            // Correlation id (optional but useful)
            var correlationId = httpContext.Request.Headers["X-Correlation-ID"].ToString();
            if (string.IsNullOrWhiteSpace(correlationId))
                correlationId = Guid.NewGuid().ToString("N"); // NEW fallback

            var invHttpReq = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/reserve")
            {
                Content = JsonContent.Create(reserveReq)
            };

            if (!string.IsNullOrWhiteSpace(authHeader))
                invHttpReq.Headers.TryAddWithoutValidation("Authorization", authHeader);

            invHttpReq.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId); // NEW

            var invResp = await inventoryClient.SendAsync(invHttpReq);

            if (invResp.StatusCode == HttpStatusCode.Conflict)
            {
                var details = await invResp.Content.ReadAsStringAsync();
                return Results.Conflict(new { error = "Not enough stock", details });
            }

            // Check non-success BEFORE reading JSON // FIX
            if (!invResp.IsSuccessStatusCode)
            {
                var details = await invResp.Content.ReadAsStringAsync();
                return Results.Problem(
                    title: "Inventory reserve failed",
                    detail: details,
                    statusCode: (int)HttpStatusCode.BadGateway);
            }

            reserveRespDto = await invResp.Content.ReadFromJsonAsync<InventoryReserveResponse>();

            if (reserveRespDto?.Lines is null || reserveRespDto.Lines.Count == 0)
            {
                return Results.Problem(
                    title: "Inventory reserve returned empty response",
                    statusCode: (int)HttpStatusCode.BadGateway);
            }
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Inventory service unreachable",
                detail: ex.Message,
                statusCode: (int)HttpStatusCode.BadGateway);
        }
        catch (TaskCanceledException ex)
        {
            return Results.Problem(
                title: "Inventory reserve timed out",
                detail: ex.Message,
                statusCode: (int)HttpStatusCode.BadGateway);
        }

        var reservedBySku = reserveRespDto!.Lines
            .ToDictionary(x => x.Sku.Trim().ToUpperInvariant(), x => x);

        // Safety: ensure every requested sku exists in reserve response
        foreach (var line in reserveLines)
        {
            if (!reservedBySku.ContainsKey(line.Sku))
                return Results.Problem(title: "Inventory reserve missing sku", detail: line.Sku, statusCode: 502);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            PubId = pubId,
            Status = OrderStatus.Pending,
            Items = reserveLines.Select(l =>
            {
                var r = reservedBySku[l.Sku];
                return new OrderItem
                {
                    Id = Guid.NewGuid(),
                    Sku = r.Sku, // NEW
                    Name = r.Name.Trim(),
                    Quantity = l.Quantity,
                    UnitPrice = r.UnitPrice // snapshot from Inventory
                };
            }).ToList()
        };

        order.Total = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Outbox message after DB commit
        var evt = new OrdersApi.Events.OrderCreatedEvent(order.Id, order.PubId, DateTimeOffset.UtcNow, order.Total);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(evt);

        var corrId = httpContext.Request.Headers["X-Correlation-ID"].ToString();
        if (string.IsNullOrWhiteSpace(corrId))
            corrId = Guid.NewGuid().ToString("N"); // NEW

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Type = nameof(OrdersApi.Events.OrderCreatedEvent),
            PayloadJson = payloadJson,
            CorrelationId = corrId
        });

        await db.SaveChangesAsync();

        var response = new OrderResponse(
            order.Id,
            order.DisplayNumber,
            order.PubId,
            order.Total,
            order.Status,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemResponse(i.Sku, i.Name, i.Quantity, i.UnitPrice)).ToList()
        );


        return Results.Created($"/api/orders/{order.Id}", response);
    })
    .WithTags("Orders");

// GET /api/orders/{id} (ללקוח/צוות)
app.MapGet("/api/orders/{id:guid}", [Authorize] async (
        Guid id,
        ClaimsPrincipal user,
        OrdersDbContext db) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.PubId == pubId);

        if (order is null)
            return Results.NotFound();

        var response = new OrderResponse(
            order.Id,
            order.DisplayNumber,
            order.PubId,
            order.Total,
            order.Status,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemResponse(i.Sku, i.Name, i.Quantity, i.UnitPrice)).ToList()
        );

        return Results.Ok(response);
    })
    .WithTags("Orders");

// PATCH /api/orders/{id}/status (צוות בר / מנהל)
app.MapPatch("/api/orders/{id:guid}/status", [Authorize] async (
        Guid id,
        string status,
        ClaimsPrincipal user,
        OrdersDbContext db) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        var role = GetRole(user);
        if (role is not ("Admin" or "PubOwner" or "Staff"))
            return Results.Forbid();

        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var newStatus))
            return Results.BadRequest(new { error = "Invalid status." });

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.PubId == pubId);
        if (order is null)
            return Results.NotFound();

        order.Status = newStatus;
        await db.SaveChangesAsync();

        return Results.Ok(new { order.Id, order.DisplayNumber, order.Status });
    })
    .WithTags("Orders");

app.Run();
