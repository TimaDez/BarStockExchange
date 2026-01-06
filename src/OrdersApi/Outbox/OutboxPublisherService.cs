using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;

namespace OrdersApi.Outbox;

public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<OutboxPublisherService> _logger;

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

        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(20)
            .ToListAsync(ct);

        if (!messages.Any()) 
            return;

        foreach (var message in messages)
        {
            try
            {             
                var resultOk = await _publisher.PublishAsync(message.Type, message, message.CorrelationId);

                if(resultOk)
                {
                    message.PublishedAtUtc = DateTimeOffset.UtcNow;
                    message.PublishAttempts++;
                    message.LastError = null;
                    
                    _logger.LogInformation("Message {MessageId} published.", message.Id);
                }
                else
                {
                    message.PublishAttempts++;
                    message.LastError = "Failed to publish message to RabbitMQ";
                    _logger.LogError("Failed to publish message {MessageId}", message.Id);
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message {MessageId}", message.Id);
                message.PublishAttempts++;
                message.LastError = ex.Message;
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }
}