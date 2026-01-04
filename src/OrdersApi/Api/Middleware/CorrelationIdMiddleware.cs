using Serilog.Context;

namespace OrdersApi.Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. קבלה או יצירה של ID
        var correlationId = GetCorrelationId(context);

        // 2. החזרה ב-Response Headers (כדי שנוכל לראות אותו ב-Postman/Client)
        if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
        {
            context.Response.Headers.Append(CorrelationIdHeaderName, correlationId);
        }

        // 3. דחיפה ל-LogContext - כל לוג שייכתב מכאן והלאה יכיל את ה-ID הזה
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        // האם ה-Gateway כבר שלח לנו ID?
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var stringValues))
        {
            return stringValues.FirstOrDefault() ?? Guid.NewGuid().ToString();
        }

        return Guid.NewGuid().ToString();
    }
}