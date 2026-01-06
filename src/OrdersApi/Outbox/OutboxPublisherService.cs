using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;

namespace OrdersApi.Outbox;

public class OutboxPublisherService : BackgroundService
{
    #region Private members

    private const int BatchSize = 20;
    private const int MaxPublishAttempts = 10;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<OutboxPublisherService> _logger;

    #endregion

    public OutboxPublisherService(
        IServiceProvider serviceProvider,
        RabbitMqPublisher publisher,
        ILogger<OutboxPublisherService> logger)
    {
        _serviceProvider = serviceProvider;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxPublisherService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in Outbox loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var now = DateTimeOffset.UtcNow; // NEW

         // NEW: only take messages that are due, not dead-lettered, and under max attempts
        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m =>
                m.PublishedAtUtc == null &&
                m.DeadLetteredAtUtc == null &&                         // NEW
                m.PublishAttempts < MaxPublishAttempts &&              // NEW
                (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)) // NEW
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)                                          // CHANGED (const)
            .ToListAsync(ct);
        
        if (!messages.Any()) 
            return;

        foreach (var message in messages)
        {
            try
            {             
                var resultOk = await _publisher.PublishAsync(message.Type, message);
                message.PublishAttempts++;

                if(resultOk)
                {
                    message.PublishedAtUtc = DateTimeOffset.UtcNow;
                    message.LastError = null;
                    message.NextAttemptAtUtc = null;
                    
                    _logger.LogInformation("Message {MessageId} published.", message.Id);
                    continue;
                }
                
                // NEW: schedule next retry with backoff (or dead-letter)
                if (message.PublishAttempts >= MaxPublishAttempts)
                {
                    message.DeadLetteredAtUtc = DateTimeOffset.UtcNow;
                    message.NextAttemptAtUtc = null;
                    message.LastError = "Max publish attempts reached. Message moved to dead-letter state.";

                    _logger.LogError(
                        "Message {MessageId} moved to dead-letter after {Attempts} attempts.",
                        message.Id,
                        message.PublishAttempts);

                    continue;
                }

                var delay = ComputeBackoffDelay(message.PublishAttempts);
                message.NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(delay);
                message.LastError = $"Publish failed. Next attempt in {delay.TotalSeconds:0}s.";

                _logger.LogWarning(
                    "Failed to publish message {MessageId}. Attempt {Attempt}/{MaxAttempts}. Next attempt at {NextAttemptAtUtc}.",
                    message.Id,
                    message.PublishAttempts,
                    MaxPublishAttempts,
                    message.NextAttemptAtUtc);
            }
            catch (Exception ex)
            {
                // CHANGED: also counts as an attempt and schedules backoff/dead-letter
                message.PublishAttempts++;
                message.LastError = ex.Message;

                if (message.PublishAttempts >= MaxPublishAttempts)
                {
                    message.DeadLetteredAtUtc = DateTimeOffset.UtcNow;
                    message.NextAttemptAtUtc = null;

                    _logger.LogError(
                        ex,
                        "Message {MessageId} moved to dead-letter after {Attempts} attempts (exception).",
                        message.Id,
                        message.PublishAttempts);

                    continue;
                }

                var delay = ComputeBackoffDelay(message.PublishAttempts);
                message.NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(delay);

                _logger.LogError(
                    ex,
                    "Exception while publishing message {MessageId}. Attempt {Attempt}/{MaxAttempts}. Next attempt at {NextAttemptAtUtc}.",
                    message.Id,
                    message.PublishAttempts,
                    MaxPublishAttempts,
                    message.NextAttemptAtUtc);
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    // NEW: exponential backoff with cap + small jitter
    private static TimeSpan ComputeBackoffDelay(int attempt)
    {
        // attempt starts at 1
        var baseSeconds = 5;
        var maxSeconds = 300; // 5 minutes cap

        var expSeconds = baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var capped = Math.Min(maxSeconds, expSeconds);

        // jitter 0-1000ms
        var jitterMs = Random.Shared.Next(0, 1000);

        return TimeSpan.FromSeconds(capped) + TimeSpan.FromMilliseconds(jitterMs);
    }
}