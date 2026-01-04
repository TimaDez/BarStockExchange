using System.Net.Http.Headers;

namespace OrdersApi.Api.Http;

public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;

        if (context != null)
        {
            // שליפת ה-ID מהבקשה הנוכחית
            if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationId))
            {
                // הזרקת ה-ID לבקשה היוצאת (ל-Inventory)
                if (!request.Headers.Contains(CorrelationIdHeaderName))
                {
                    request.Headers.Add(CorrelationIdHeaderName, correlationId.FirstOrDefault());
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}