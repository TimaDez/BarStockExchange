namespace OrdersApi.Contracts;

// Orders -> Inventory
public sealed record InventoryReserveRequested(
    Guid OrderId,
    Guid PubId,
    List<InventoryReserveLine> Lines,
    DateTimeOffset RequestedAtUtc);

// Inventory -> Orders
public sealed record InventoryReserveSucceeded(
    Guid OrderId,
    Guid ReservationId,
    List<InventoryReservedLine> Lines,
    DateTimeOffset ReservedAtUtc);

// Inventory -> Orders
public sealed record InventoryReserveFailed(
    Guid OrderId,
    string Reason,
    DateTimeOffset FailedAtUtc);

// Shared lines
// public sealed record InventoryReserveLine(string Sku, int Quantity);
//public sealed record InventoryReservedLine(string Sku, string Name, decimal UnitPrice, int Quantity);
