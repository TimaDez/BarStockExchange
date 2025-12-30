using OrdersApi.Models;

namespace OrdersApi.Contracts;

public record OrderItemResponse(string Name, int Quantity, decimal UnitPrice);

public record OrderResponse(
  Guid Id,
  int DisplayNumber,
  Guid PubId,
  OrderStatus Status,
  DateTimeOffset CreatedAt,
  List<OrderItemResponse> Items);
