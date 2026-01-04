using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Api.Extensions;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Outbox; // NEW
using OrdersApi.Application.Orders.CreateOrder; // NEW

namespace OrdersApi.Api.Endpoints;

public static class OrdersEndpoints
{
  #region Methods

  public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/orders")
      .WithTags("Orders")
      .RequireAuthorization(); // NEW (במקום [Authorize] על כל endpoint)

    // POST /api/orders (לקוח)
    group.MapPost("/", async (
      CreateOrderRequest req,
      System.Security.Claims.ClaimsPrincipal user,
      CreateOrderHandler handler, // NEW
      HttpContext httpContext,
      CancellationToken ct // NEW
    ) =>
    {
      if (!user.TryGetPubId(out var pubId))
        return Results.Unauthorized();

      var authHeader = httpContext.Request.Headers.Authorization.ToString(); // NEW

      var correlationId = httpContext.Request.Headers["X-Correlation-ID"].ToString(); // NEW
      if (string.IsNullOrWhiteSpace(correlationId))
        correlationId = Guid.NewGuid().ToString("N"); // NEW

      var result = await handler.HandleAsync(pubId, req, authHeader, correlationId, ct); // NEW

      if (result.IsSuccess)
        return Results.Created($"/api/orders/{result.Response!.Id}", result.Response);

      var map = new Dictionary<CreateOrderErrorCode, Func<IResult>> // NEW
      {
        [CreateOrderErrorCode.Unauthorized] = () => Results.Unauthorized(),
        [CreateOrderErrorCode.ValidationFailed] = () => Results.BadRequest(new { error = result.ErrorMessage }),
        [CreateOrderErrorCode.InventoryConflict] = () =>
          Results.Conflict(new { error = result.ErrorMessage, details = result.ErrorDetails }),
        [CreateOrderErrorCode.InventoryTimeout] = () =>
          Results.Problem(title: result.ErrorMessage, detail: result.ErrorDetails, statusCode: 502),
        [CreateOrderErrorCode.InventoryUnreachable] = () =>
          Results.Problem(title: result.ErrorMessage, detail: result.ErrorDetails, statusCode: 502),
        [CreateOrderErrorCode.InventoryBadGateway] = () =>
          Results.Problem(title: result.ErrorMessage, detail: result.ErrorDetails, statusCode: 502),
        [CreateOrderErrorCode.InventoryMissingSku] = () =>
          Results.Problem(title: result.ErrorMessage, detail: result.ErrorDetails, statusCode: 502),
        [CreateOrderErrorCode.PersistenceFailed] = () =>
          Results.Problem(title: result.ErrorMessage, detail: result.ErrorDetails, statusCode: 500),
      };

      if (result.ErrorCode is null)
        return Results.Problem(title: "Unknown error", statusCode: 500);

      return map.TryGetValue(result.ErrorCode.Value, out var factory)
        ? factory()
        : Results.Problem(title: "Unhandled error", statusCode: 500);
    });

    // GET /api/orders/{id}
    group.MapGet("/{id:guid}", async (
      Guid id,
      System.Security.Claims.ClaimsPrincipal user,
      OrdersDbContext db) =>
    {
      if (!user.TryGetPubId(out var pubId))
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
    });

    // PATCH /api/orders/{id}/status
    group.MapPatch("/{id:guid}/status", async (
      Guid id,
      string status,
      System.Security.Claims.ClaimsPrincipal user,
      OrdersDbContext db) =>
    {
      if (!user.TryGetPubId(out var pubId))
        return Results.Unauthorized();

      var role = user.GetRole();
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
    });

    return app;
  }

  #endregion
}
