using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OrdersApi.Application.Orders.CreateOrder;
using OrdersApi.Contracts;
using OrdersApi.Api.Extensions; // מוודא שיש לך גישה ל-Extensions

namespace OrdersApi.Api.Endpoints;

public static class OrdersEndpoints
{
    public static void MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orders", HandleCreateOrder)
           .WithName("CreateOrder")
           .RequireAuthorization(); // מוודא שהמשתמש מחובר
    }

    private static async Task<IResult> HandleCreateOrder(
        [FromBody] CreateOrderRequest request,
        ClaimsPrincipal user,
        CreateOrderHandler handler,
        CancellationToken ct)
    {
        var pubId = user.GetPubId();

        var result = await handler.HandleAsync(request, pubId, ct);

        if (result.IsSuccess && result.Response != null)
        {
            // CHANGED: Saga → async completion. Return 202 Accepted.
            // The client should poll GET /api/orders/{id} to see when status becomes Ready/Cancelled.
            var location = $"/api/orders/{result.Response.Id}";
            return Results.Accepted(location, result.Response);
        }

        return Results.Json(new 
        { 
            error = result.ErrorMessage, 
            details = result.ErrorDetails 
        }, statusCode: 400); // או קוד שגיאה אחר לפי ה-ErrorCode
    }
}