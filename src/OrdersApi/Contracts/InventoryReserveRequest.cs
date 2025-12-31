namespace OrdersApi.Contracts;

// NEW: Contract that matches InventoryApi.Contracts.ReserveRequest
// We keep it here to avoid a project reference between services.
public sealed record InventoryReserveRequest(List<InventoryReserveLine> Lines);

public sealed record InventoryReserveLine(string Sku, int Quantity);
