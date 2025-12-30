namespace InventoryApi.Contracts;

public sealed record UpsertInventoryRequest(string Sku, string Name, int Quantity);
