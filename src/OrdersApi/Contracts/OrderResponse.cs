namespace OrdersApi.Contracts;

public sealed record OrderResponse(
  Guid Id,
  long DisplayNumber,
  Guid PubId,
  decimal Total,
  Models.OrderStatus Status,
  DateTimeOffset CreatedAt,
  List<OrderItemResponse> Items
);

public sealed record OrderItemResponse(
  string Sku, // NEW
  string Name,
  int Quantity,
  decimal UnitPrice
);
