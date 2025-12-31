namespace OrdersApi.Models;

public sealed class Order
{
  public Guid Id { get; set; }
  public Guid PubId { get; set; }
  // מספר הזמנה “תצוגתי” (נוח ללקוח). כרגע GUID מספיק, אבל נשאיר מקום.
  public int DisplayNumber { get; set; }
  public OrderStatus Status { get; set; } = OrderStatus.Pending;
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public List<OrderItem> Items { get; set; } = new();
  public string? ClientRequestId { get; set; }

}
