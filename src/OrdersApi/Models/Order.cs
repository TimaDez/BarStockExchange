namespace OrdersApi.Models;

public sealed class Order
{
  public Guid Id { get; set; }

  public Guid PubId { get; set; }

  public int DisplayNumber { get; set; }
  public OrderStatus Status { get; set; } = OrderStatus.PendingReservation;
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public List<OrderItem> Items { get; set; } = new();
  public string? ClientRequestId { get; set; }
  public decimal Total { get; set; }
}
