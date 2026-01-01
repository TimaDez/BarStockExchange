namespace OrdersApi.Contracts;

// Request to InventoryApi
public sealed record InventoryReserveRequest(List<InventoryReserveLine> Lines);

public sealed record InventoryReserveLine(string Sku, int Quantity);

// Response from InventoryApi (pricing snapshot)
public sealed record InventoryReserveResponse(List<InventoryReservedLine> Lines);

public sealed record InventoryReservedLine(string Sku, string Name, decimal UnitPrice, int Quantity);
