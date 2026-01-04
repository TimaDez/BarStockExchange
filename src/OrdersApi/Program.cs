using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer; // NEW: חובה בשביל JWT
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens; // NEW: חובה בשביל הטוקן
using Microsoft.OpenApi.Models; // NEW: חובה בשביל Swagger Authorize
using OrdersApi.Api.Endpoints;
using OrdersApi.Api.Extensions;
using OrdersApi.Api.Http;
using OrdersApi.Api.Middleware;
using OrdersApi.Application.Orders.CreateOrder;
using OrdersApi.Data;
using OrdersApi.Outbox;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddEndpointsApiExplorer();

// NEW: הגדרת Swagger עם תמיכה ב-Bearer Token
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OrdersApi", Version = "v1" });

    // הגדרת כפתור ה-Authorize
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.\r\n\r\nEnter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
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

// NEW: הגדרת אימות (Authentication) - קריאת מפתח ה-JWT מהקונפיגורציה
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"];
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!))
        };
    });

builder.Services.AddAuthorization(); // חובה

// DB Context
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// RabbitMQ & Outbox
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();

// Http Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
builder.Services.AddTransient<AuthenticationDelegatingHandler>();

builder.Services.AddHttpClient("InventoryClient", client =>
{
    // משתמש בכתובת שמוגדרת ב-docker-compose או ב-InventoryBaseUrl
    var baseUrl = builder.Configuration["Services:InventoryBaseUrl"] ?? "http://inventoryapi:8080";
    client.BaseAddress = new Uri(baseUrl);
})
.AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
.AddHttpMessageHandler<AuthenticationDelegatingHandler>();

builder.Services.AddScoped<CreateOrderHandler>();

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

// NEW: סדר ה-Middleware קריטי!
app.UseAuthentication(); // 1. קודם מזהים מי המשתמש
app.UseAuthorization();  // 2. אחר כך בודקים הרשאות

// Migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    // הוספתי try-catch למקרה שה-DB עוד לא עלה
    try { await db.Database.MigrateAsync(); } catch { }
}

app.MapHealthEndpoints();
app.MapOrdersEndpoints();

app.Run();