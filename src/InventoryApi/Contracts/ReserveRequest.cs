namespace InventoryApi.Contracts;

public sealed record ReserveRequest(List<ReserveLine> Lines, string? RequestId = null); // NEW

public sealed record ReserveLine(string Sku, int Quantity);

