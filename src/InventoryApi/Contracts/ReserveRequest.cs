namespace InventoryApi.Contracts;

public sealed record ReserveRequest(List<ReserveLine> Lines);

public sealed record ReserveLine(string Sku, int Quantity);

// NEW: reserve response carries pricing snapshot for OrdersApi
public sealed record ReserveResponse(List<ReservedLine> Lines);

public sealed record ReservedLine(string Sku, string Name, decimal UnitPrice, int Quantity);
