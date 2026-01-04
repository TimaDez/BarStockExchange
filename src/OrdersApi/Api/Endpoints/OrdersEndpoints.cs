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
        // חילוץ ה-ID בצורה נקייה (בהנחה שיש לך את ה-Extension הזה)
        // אם אין לך, תחליף ב: Guid.Parse(user.FindFirst("pub_id")?.Value)
        var pubId = user.GetPubId();

        // התיקון: קריאה ל-Handler בלי headers ובסדר הנכון
        var result = await handler.HandleAsync(request, pubId, ct);

        if (result.IsSuccess && result.Response != null)
        {
            return Results.Ok(result.Response);
        }

        return Results.Json(new 
        { 
            error = result.ErrorMessage, 
            details = result.ErrorDetails 
        }, statusCode: 400); // או קוד שגיאה אחר לפי ה-ErrorCode
    }
}