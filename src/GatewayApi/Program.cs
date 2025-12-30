using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Reverse proxy (YARP) loads routes/clusters from appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions());
app.MapGet("/", () => Results.Text("Gateway API is running", "text/plain"));

app.MapReverseProxy();

app.Run();
