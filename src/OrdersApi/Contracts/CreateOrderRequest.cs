namespace OrdersApi.Contracts;

public sealed record CreateOrderRequest(
  List<CreateOrderItemRequest> Items,
  string? RequestId = null
);

public sealed record CreateOrderItemRequest(
  string Sku,
  int Quantity
);
