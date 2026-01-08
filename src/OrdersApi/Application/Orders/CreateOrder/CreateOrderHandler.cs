using System.Text.Json;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Outbox;

namespace OrdersApi.Application.Orders.CreateOrder;

public class CreateOrderHandler
{
    #region Private members
    private const string InventoryReserveRequestedRoutingKey = "inventory.reserve.requested.v1"; // NEW

    private readonly OrdersDbContext _dbContext;
    private readonly ILogger<CreateOrderHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    #endregion

    #region Methods
    public CreateOrderHandler(
        OrdersDbContext dbContext,
        ILogger<CreateOrderHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    // CHANGED: userId here is actually pubId (as you pass user.GetPubId() from endpoint)
    public async Task<CreateOrderResult> HandleAsync(CreateOrderRequest request, Guid pubId, CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting CreateOrder (SAGA) for Pub {PubId} with {ItemCount} items",
            pubId,
            request.Items?.Count ?? 0);

        #region 1. Validation & Grouping
        if (request.Items == null || request.Items.Count == 0)
        {
            return CreateOrderResult.Fail(
                CreateOrderErrorCode.EmptyOrder,
                "Order must contain at least one item.");
        }

        var groupedItems = request.Items
            .GroupBy(x => x.Sku)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        #endregion

        #region 2. Create Order (Pending) WITHOUT calling Inventory (SAGA) // CHANGED
        var orderId = Guid.NewGuid();

        // NEW: create items only with SKU+Quantity. Name/UnitPrice will be filled when InventoryReserveSucceeded arrives.
        var orderItems = groupedItems
            .Select(kvp => new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Sku = kvp.Key,
                Quantity = kvp.Value,
                Name = string.Empty,     // NEW (unknown yet)
                UnitPrice = 0m           // NEW (unknown yet)
            })
            .ToList();

        var order = new Order
        {
            Id = orderId,
            PubId = pubId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.PendingReservation,
            Items = orderItems,
            Total = 0m, // NEW: will be calculated after reservation succeeds
            DisplayNumber = 0
        };

        _dbContext.Orders.Add(order);
        #endregion

        #region 3. Write Outbox message: InventoryReserveRequested (SAGA) // NEW
        var correlationId = _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault();

        var reserveLines = groupedItems
            .Select(kvp => new InventoryReserveLine(kvp.Key, kvp.Value))
            .ToList();

        var reserveRequestedEvent = new InventoryReserveRequested(
            OrderId: order.Id,
            PubId: order.PubId,
            Lines: reserveLines,
            RequestedAtUtc: DateTimeOffset.UtcNow);

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,

            // IMPORTANT:
            // Your OutboxPublisherService calls PublishAsync(message.Type, message, message.CorrelationId)
            // and uses message.Type as the routingKey.
            Type = InventoryReserveRequestedRoutingKey, // CHANGED (routing key)

            PayloadJson = JsonSerializer.Serialize(reserveRequestedEvent),
            CorrelationId = correlationId,
            PublishAttempts = 0,
            PublishedAtUtc = null,
            LastError = null
        };

        _dbContext.Set<OutboxMessage>().Add(outboxMessage);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} created in status {Status}. ReserveRequested event queued (Outbox). CorrelationId={CorrelationId}",
            order.Id,
            order.Status,
            correlationId);
        #endregion

        #region 4. Return response (Pending) // CHANGED
        // We return a "pending" order. Client can poll GET /api/orders/{id} later to see Ready/Cancelled.
        var responseDto = new OrderResponse(
            order.Id,
            order.DisplayNumber,
            order.PubId,
            order.Total,
            order.Status,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemResponse(
                i.Sku,
                i.Name,      // empty for now
                i.Quantity,
                i.UnitPrice  // 0 for now
            )).ToList()
        );

        return CreateOrderResult.Ok(responseDto);
        #endregion
    }
    #endregion
}
