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

        var now = DateTimeOffset.UtcNow;

         // NEW: only take messages that are due, not dead-lettered, and under max attempts
        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m =>
                m.PublishedAtUtc == null &&
                m.DeadLetteredAtUtc == null &&                      
                m.PublishAttempts < MaxPublishAttempts &&           
                (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)) 
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)                                          
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
                
                if (TryDeadLetter(message, lastError: "Max publish attempts reached. Message moved to dead-letter state."))
                    continue;

                SetDelayOnPublishFailed(message, now);
            }
            catch (Exception ex)
            {
                // CHANGED: also counts as an attempt and schedules backoff/dead-letter
                message.PublishAttempts++;
                message.LastError = ex.Message;

                if (TryDeadLetter(message, ex))
                    continue;

                SetDelayOnPublishException(message, ex, now);
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }

    private bool TryDeadLetter(OutboxMessage message, Exception? ex = null, string? lastError = null)
    {
        if (message.PublishAttempts < MaxPublishAttempts)
            return false;

        message.DeadLetteredAtUtc = DateTimeOffset.UtcNow;
        message.NextAttemptAtUtc = null;

        if (!string.IsNullOrWhiteSpace(lastError))
            message.LastError = lastError;

        if (ex is null)
        {
            _logger.LogError(
                "Message {MessageId} moved to dead-letter after {Attempts} attempts.",
                message.Id,
                message.PublishAttempts);
        }
        else
        {
            _logger.LogError(
                ex,
                "Message {MessageId} moved to dead-letter after {Attempts} attempts (exception).",
                message.Id,
                message.PublishAttempts);
        }

        return true;
    }

    private void SetDelayOnPublishFailed(OutboxMessage message, DateTimeOffset? now = null)
    {
        var utcNow = now ?? DateTimeOffset.UtcNow;

        var delay = ComputeBackoffDelay(message.PublishAttempts);
        message.NextAttemptAtUtc = utcNow.Add(delay);
        message.LastError = $"Publish failed. Next attempt in {delay.TotalSeconds:0}s.";

        _logger.LogWarning(
            "Failed to publish message {MessageId}. Attempt {Attempt}/{MaxAttempts}. Next attempt at {NextAttemptAtUtc}.",
            message.Id,
            message.PublishAttempts,
            MaxPublishAttempts,
            message.NextAttemptAtUtc);
    }

    private void SetDelayOnPublishException(OutboxMessage message, Exception ex, DateTimeOffset? now = null)
    {
        var utcNow = now ?? DateTimeOffset.UtcNow;

        var delay = ComputeBackoffDelay(message.PublishAttempts);
        message.NextAttemptAtUtc = utcNow.Add(delay);

        _logger.LogError(
            ex,
            "Exception while publishing message {MessageId}. Attempt {Attempt}/{MaxAttempts}. Next attempt at {NextAttemptAtUtc}.",
            message.Id,
            message.PublishAttempts,
            MaxPublishAttempts,
            message.NextAttemptAtUtc);
    }

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