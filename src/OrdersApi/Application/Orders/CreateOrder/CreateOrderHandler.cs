using System.Text.Json;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Outbox;

namespace OrdersApi.Application.Orders.CreateOrder;

public class CreateOrderHandler
{
    private readonly OrdersDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor; // NEW: בשביל לשמור CorrelationId ב-Outbox

    public CreateOrderHandler(
        OrdersDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<CreateOrderHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<CreateOrderResult> HandleAsync(CreateOrderRequest request, Guid userId, CancellationToken ct)
    {
        _logger.LogInformation("Starting CreateOrder for User {UserId} with {ItemCount} items", userId, request.Items.Count);

        #region 1. Validation & Grouping
        if (request.Items == null || !request.Items.Any())
        {
            return CreateOrderResult.Fail(CreateOrderErrorCode.EmptyOrder, "Order must contain at least one item.");
        }

        var groupedItems = request.Items
            .GroupBy(x => x.Sku)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        #endregion

        #region 2. Reserve Stock (Inventory - Batch Request)
        var reservationLines = groupedItems
            .Select(x => new InventoryReserveLine(x.Key, x.Value))
            .ToList();

        var reserveRequest = new InventoryReserveRequest(reservationLines);
        var inventoryClient = _httpClientFactory.CreateClient("InventoryClient");
        List<OrderItem> orderItems = new();

        try
        {
            var response = await inventoryClient.PostAsJsonAsync("/api/inventory/reserve", reserveRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inventory batch reservation failed. Status: {StatusCode}", response.StatusCode);
                return CreateOrderResult.Fail(CreateOrderErrorCode.InventoryError, "Inventory reservation failed.");
            }

            var reservationResult = await response.Content.ReadFromJsonAsync<InventoryReserveResponse>(cancellationToken: ct);
            
            if (reservationResult == null || reservationResult.Lines == null)
            {
                 return CreateOrderResult.Fail(CreateOrderErrorCode.InventoryError, "Invalid response from inventory.");
            }

            foreach (var reservedLine in reservationResult.Lines)
            {
                orderItems.Add(new OrderItem
                {
                    Sku = reservedLine.Sku,
                    Name = reservedLine.Name,
                    Quantity = reservedLine.Quantity,
                    UnitPrice = reservedLine.UnitPrice
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error calling Inventory service");
            return CreateOrderResult.Fail(CreateOrderErrorCode.InventoryError, "System error during reservation.");
        }
        #endregion

        #region 3. Create Order & Outbox
        
        var order = new Order
        {
            Id = Guid.NewGuid(),
            PubId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.Pending,
            Items = orderItems,
            Total = orderItems.Sum(x => x.Quantity * x.UnitPrice),
            DisplayNumber = 0 // ה-DB יטפל בזה אם זה Identity, או שנשאיר כ-0 כרגע
        };

        _dbContext.Orders.Add(order);

        // יצירת האירוע
        var orderCreatedEvent = new Events.OrderCreatedEvent(
            order.Id,
            order.PubId,
            order.CreatedAt,
            order.Total
        );

        // שליפת ה-CorrelationId הנוכחי
        var correlationId = _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault();

        // יצירת הודעת Outbox ידנית (כי אין מתודה סטטית במחלקה החדשה ששלחת)
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Type = orderCreatedEvent.GetType().Name,
            PayloadJson = JsonSerializer.Serialize(orderCreatedEvent),
            CorrelationId = correlationId, // NEW: שמירת ה-ID
            PublishAttempts = 0,
            PublishedAtUtc = null
        };

        _dbContext.Set<OutboxMessage>().Add(outboxMessage);

        await _dbContext.SaveChangesAsync(ct);
        
        _logger.LogInformation("Order {OrderId} created successfully. Total: {Total}", order.Id, order.Total);
        #endregion

        var responseDto = new OrderResponse(
            order.Id,
            order.DisplayNumber,
            order.PubId,
            order.Total,
            order.Status,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemResponse(
                i.Sku, 
                i.Name, 
                i.Quantity, 
                i.UnitPrice)).ToList()
        );

        return CreateOrderResult.Ok(responseDto);
    }
}