namespace InventoryApi.Models;

public sealed class InventoryItem
{
  public Guid Id { get; set; }
  public Guid PubId { get; set; }
  public string Sku { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public int Quantity { get; set; }
  public decimal UnitPrice { get; set; }
  public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
