using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;

namespace OrdersApi.Outbox;

// NEW
public sealed class OutboxPublisherService : BackgroundService
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<OutboxPublisherService> _logger;

  public OutboxPublisherService(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PublishOnce(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Outbox publish failed");
      }

      await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
    }
  }

  private async Task PublishOnce(CancellationToken ct)
  {
    await using var scope = _scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    var publisher = scope.ServiceProvider.GetRequiredService<IRabbitMqPublisher>();

    var pending = await db.OutboxMessages
      .Where(x => x.PublishedAtUtc == null)
      .OrderBy(x => x.OccurredAtUtc)
      .Take(25)
      .ToListAsync(ct);

    if (pending.Count == 0)
      return;

    foreach (var msg in pending)
    {
      ct.ThrowIfCancellationRequested();

      try
      {
        msg.PublishAttempts++;
        msg.LastError = null;

        var routingKey = msg.Type.ToLowerInvariant();
        await publisher.PublishAsync(routingKey, msg.Id.ToString("N"), msg.PayloadJson, msg.CorrelationId, ct);

        msg.PublishedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Outbox published. Id={Id} Type={Type}", msg.Id, msg.Type);
      }
      catch (Exception ex)
      {
        msg.LastError = ex.Message;
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(ex, "Outbox publish attempt failed. Id={Id} Type={Type} Attempt={Attempt}", msg.Id, msg.Type,
          msg.PublishAttempts);
      }
    }
  }
}
