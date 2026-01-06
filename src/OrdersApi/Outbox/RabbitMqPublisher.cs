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
    public async Task<bool> PublishAsync(string routingKey, OutboxMessage message, string? correlationId)
    {
        await EnsureConnectionAsync();

        if (_channel is not { IsOpen: true })
        {
            _logger.LogWarning("Cannot publish message, channel is closed.");
            return false;
        }

        var body = Encoding.UTF8.GetBytes(message.PayloadJson);

        // יצירת Properties ושמירת ה-CorrelationId
        var props = new BasicProperties
        {
            CorrelationId = correlationId,
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ");
            return false;
        }

        _logger.LogInformation("Published message '{RoutingKey}' with CorrelationId '{CorrelationId}'", routingKey, correlationId);
        return true;
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
            _logger.LogError(ex, "Could not connect to RabbitMQ.");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.CloseAsync();
        if (_connection != null) await _connection.CloseAsync();
    }
}