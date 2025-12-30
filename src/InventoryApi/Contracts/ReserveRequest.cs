namespace InventoryApi.Contracts;

public sealed record ReserveRequest(List<ReserveLine> Lines);

public sealed record ReserveLine(string Sku, int Quantity);
