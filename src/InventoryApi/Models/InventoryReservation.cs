namespace InventoryApi.Models;

public sealed class InventoryReservation
{
    public Guid Id { get; set; }
    public Guid PubId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
