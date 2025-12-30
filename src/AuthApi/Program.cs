using System.Security.Claims;
using System.Text;
using AuthApi.Contracts;
using AuthApi.Data;
using AuthApi.Models;
using AuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Db");
    options.UseNpgsql(cs);
});

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
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
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(15)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Text("Auth API is running", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/auth/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

// DB migrate + seed (לשלב לימודי זה בסדר; בפרודקשן עושים migration step נפרד)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();

    // Seed: Pub אחד + Admin אחד אם DB ריק
    if (!await db.Pubs.AnyAsync())
    {
        var pub = new Pub { Id = Guid.NewGuid(), Name = "Default Pub" };
        db.Pubs.Add(pub);

        var admin = new AuthUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123"),
            IsActive = true
        };
        db.Users.Add(admin);

        db.UserPubs.Add(new UserPub
        {
            UserId = admin.Id,
            PubId = pub.Id,
            Role = UserRole.Admin
        });

        await db.SaveChangesAsync();
    }
}

app.MapPost("/api/auth/login", async (LoginRequest req, AuthDbContext db, JwtTokenService jwtSvc) =>
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var user = await db.Users
            .Include(x => x.UserPubs)
            .FirstOrDefaultAsync(x => x.Email.ToLower() == email);

        if (user is null || !user.IsActive)
            return Results.Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Results.Unauthorized();

        // כרגע בוחרים את הפאב הראשון (בהמשך נוסיף בחירה)
        var userPub = user.UserPubs.FirstOrDefault();
        if (userPub is null)
            return Results.Unauthorized();

        var token = jwtSvc.CreateToken(user, userPub);
        return Results.Ok(new LoginResponse(token));
    })
    .WithTags("Auth")
    .AllowAnonymous();

app.MapGet("/api/auth/me", [Authorize](ClaimsPrincipal user) =>
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? "";
        var email = user.FindFirstValue("email") ?? "";
        var role = user.FindFirstValue("role") ?? "";
        var pubId = user.FindFirstValue("pub_id") ?? "";

        if (!Guid.TryParse(userId, out var uid) || !Guid.TryParse(pubId, out var pid))
            return Results.Unauthorized();

        return Results.Ok(new MeResponse(uid, email, role, pid));
    })
    .WithTags("Auth");

app.Run();
