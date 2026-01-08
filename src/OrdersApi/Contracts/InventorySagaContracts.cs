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
