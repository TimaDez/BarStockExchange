using System.Security.Claims;
using System.Text;
using InventoryApi.Contracts;
using InventoryApi.Data;
using InventoryApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Db");
    options.UseNpgsql(cs);
});

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

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Text("Inventory API is running", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

// Apply migrations on startup (בינתיים כן)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
}

static bool TryGetPubId(ClaimsPrincipal user, out Guid pubId)
{
    var pubIdStr = user.FindFirstValue("pub_id");
    return Guid.TryParse(pubIdStr, out pubId);
}

static string? GetRole(ClaimsPrincipal user) => user.FindFirstValue("role");

// GET /api/inventory (רשימת מלאי לפאב)
app.MapGet("/api/inventory", [Authorize] async (ClaimsPrincipal user, InventoryDbContext db) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        var items = await db.Items
            .Where(x => x.PubId == pubId)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Results.Ok(items);
    })
    .WithTags("Inventory");

// PUT /api/inventory (Admin/PubOwner) upsert מוצר
app.MapPut("/api/inventory", [Authorize] async (
        UpsertInventoryRequest req,
        ClaimsPrincipal user,
        InventoryDbContext db) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        var role = GetRole(user);
        if (role is not ("Admin" or "PubOwner"))
            return Results.Forbid();

        if (string.IsNullOrWhiteSpace(req.Sku) || req.Quantity < 0)
            return Results.BadRequest(new { error = "Invalid sku/quantity" });

        var sku = req.Sku.Trim().ToUpperInvariant();
        var name = string.IsNullOrWhiteSpace(req.Name) ? sku : req.Name.Trim();

        var item = await db.Items.FirstOrDefaultAsync(x => x.PubId == pubId && x.Sku == sku);
        if (item is null)
        {
            item = new InventoryItem
            {
                Id = Guid.NewGuid(),
                PubId = pubId,
                Sku = sku,
                Name = name,
                Quantity = req.Quantity,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Items.Add(item);
        }
        else
        {
            item.Name = name;
            item.Quantity = req.Quantity;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.Ok(item);
    })
    .WithTags("Inventory");

// POST /api/inventory/reserve (פנימי להזמנות) – מוריד מלאי אם אפשר
app.MapPost("/api/inventory/reserve", [Authorize] async (
        ReserveRequest req,
        ClaimsPrincipal user,
        InventoryDbContext db) =>
    {
        if (!TryGetPubId(user, out var pubId))
            return Results.Unauthorized();

        if (req.Lines is null || req.Lines.Count == 0)
            return Results.BadRequest(new { error = "No lines" });

        await using var tx = await db.Database.BeginTransactionAsync();

        var skus = req.Lines.Select(l => l.Sku.Trim().ToUpperInvariant()).Distinct().ToList();
        var items = await db.Items
            .Where(x => x.PubId == pubId && skus.Contains(x.Sku))
            .ToListAsync();

        foreach (var line in req.Lines)
        {
            var sku = line.Sku.Trim().ToUpperInvariant();
            if (line.Quantity <= 0)
                return Results.BadRequest(new { error = $"Invalid quantity for {sku}" });

            var item = items.FirstOrDefault(x => x.Sku == sku);
            if (item is null)
                return Results.Conflict(new { error = $"Out of stock: {sku}" });

            if (item.Quantity < line.Quantity)
                return Results.Conflict(new { error = $"Not enough stock: {sku}", available = item.Quantity });
        }

        foreach (var line in req.Lines)
        {
            var sku = line.Sku.Trim().ToUpperInvariant();
            var item = items.First(x => x.Sku == sku);
            item.Quantity -= line.Quantity;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Ok(new { ok = true });
    })
    .WithTags("Inventory");

app.Run();
