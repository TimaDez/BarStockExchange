using OrdersApi.Models;

namespace OrdersApi.Contracts;

public record CreateOrderItemRequest(string Name, int Quantity, decimal UnitPrice);

public record CreateOrderRequest(List<CreateOrderItemRequest> Items);
