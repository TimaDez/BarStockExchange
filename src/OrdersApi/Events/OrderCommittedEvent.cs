namespace OrdersApi.Events;

public sealed record OrderCreatedEvent(
  Guid OrderId,
  Guid PubId,
  DateTimeOffset CreatedAtUtc,
  decimal Total
);
