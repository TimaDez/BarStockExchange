using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OrdersApi.Api.Endpoints; // NEW
using OrdersApi.Data;
using OrdersApi.Outbox;
using OrdersApi.Application.Orders.CreateOrder; // NEW

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<CreateOrderHandler>(); // NEW

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OrdersApi", Version = "v1" });

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

app.MapHealthEndpoints(); // NEW

app.UseAuthentication();
app.UseAuthorization();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.MigrateAsync();
}

app.MapOrdersEndpoints(); // NEW

app.Run();
