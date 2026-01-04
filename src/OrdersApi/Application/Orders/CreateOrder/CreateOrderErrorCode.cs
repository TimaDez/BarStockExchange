namespace OrdersApi.Application.Orders.CreateOrder;

public enum CreateOrderErrorCode
{
  Unauthorized,
  ValidationFailed,
  InventoryConflict,
  InventoryBadGateway,
  InventoryTimeout,
  InventoryUnreachable,
  InventoryMissingSku,
  PersistenceFailed,
  EmptyOrder,
  InventoryError
}
