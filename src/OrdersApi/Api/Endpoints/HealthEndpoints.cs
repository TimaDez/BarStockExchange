namespace OrdersApi.Api.Endpoints;

public static class HealthEndpoints
{
  #region Methods

  public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
  {
    app.MapGet("/", () => Results.Text("Orders API is running", "text/plain"));
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    return app;
  }

  #endregion
}
