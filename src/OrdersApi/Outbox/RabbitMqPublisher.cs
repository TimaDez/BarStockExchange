using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client; 

namespace OrdersApi.Outbox;

public class RabbitMqPublisher : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // התיקון: הוספת הפרמטר השלישי correlationId
    public async Task<bool> PublishAsync(string routingKey, OutboxMessage message)
    {
        try
        {
            await EnsureConnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ connection is not available. Cannot publish message {MessageId}", message.Id);
            return false;
        }
        
        if (_channel is not { IsOpen: true })
        {
            _logger.LogWarning("Cannot publish message, channel is closed.");
            return false;
        }

        var body = Encoding.UTF8.GetBytes(message.PayloadJson);

        // יצירת Properties ושמירת ה-CorrelationId
        var props = new BasicProperties
        {
            CorrelationId = message.CorrelationId,
            MessageId = message.Id.ToString(),
            Persistent = true
        };

        try
        {
            await _channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: props,
                body: body);
            
            _logger.LogInformation(
                "Published message '{RoutingKey}' (MessageId: {MessageId}) with CorrelationId '{CorrelationId}'",
                routingKey,
                props.MessageId,
                message.CorrelationId);

            return true;
        }
        catch (Exception ex)
        {
           // NEW: if publish fails, cleanup so next attempt reconnects cleanly
            _logger.LogError(ex, "Failed to publish message {MessageId} to RabbitMQ. Cleaning up connection.", message.Id);
            await CleanupConnectionAsync(); // NEW
            return false;
        }
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
        {
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to RabbitMQ...");
            
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                UserName = _options.User,
                Password = _options.Password,
                Port = 5672
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange, 
                type: ExchangeType.Topic, 
                durable: true);

            _logger.LogInformation("Connected to RabbitMQ successfully.");
        }
        catch (Exception ex)
        {
            // NEW: cleanup and rethrow so caller can treat as publish failure
            _logger.LogError(ex, "Could not connect to RabbitMQ.");
            await CleanupConnectionAsync();
            throw;
        }
    }

   // NEW: central cleanup for runtime reconnects + dispose
    private async Task CleanupConnectionAsync()
    {
        // Close channel first
        if (_channel != null)
        {
            try
            {
                if (_channel.IsOpen)
                    await _channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ channel.");
            }
            finally
            {
                _channel = null;
            }
        }

        // Then close connection
        if (_connection != null)
        {
            try
            {
                if (_connection.IsOpen)
                    await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ connection.");
            }
            finally
            {
                _connection = null;
            }
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        await CleanupConnectionAsync();
    }
}