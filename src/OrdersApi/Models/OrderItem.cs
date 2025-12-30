namespace OrdersApi.Models;

public sealed class OrderItem
{
  public Guid Id { get; set; }

  public Guid OrderId { get; set; }
  public Order Order { get; set; } = null!;

  public string Name { get; set; } = string.Empty;
  public int Quantity { get; set; }
  public decimal UnitPrice { get; set; }
}
