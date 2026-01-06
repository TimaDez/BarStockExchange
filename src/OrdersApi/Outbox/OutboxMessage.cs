namespace OrdersApi.Outbox;

public sealed class OutboxMessage
{
  public Guid Id { get; set; }
  public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public string Type { get; set; } = string.Empty;
  public string PayloadJson { get; set; } = string.Empty;
  public string? CorrelationId { get; set; }
  public DateTimeOffset? PublishedAtUtc { get; set; }
  public DateTimeOffset? NextAttemptAtUtc { get; set; }
  public DateTimeOffset? DeadLetteredAtUtc { get; set; }
  public int PublishAttempts { get; set; }
  public string? LastError { get; set; }
}
