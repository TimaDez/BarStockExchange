using System.Text;
using RabbitMQ.Client;
using OrdersApi.Outbox;

namespace OrdersApi.Outbox;

public interface IRabbitMqPublisher
{
  Task PublishAsync(string routingKey, string messageId, string bodyJson, string? correlationId, CancellationToken ct);
}

public sealed class RabbitMqPublisher : IRabbitMqPublisher, IAsyncDisposable // NEW
{
  private readonly RabbitMqOptions _options;
  private readonly ILogger<RabbitMqPublisher> _logger;

  private IConnection? _connection; // NEW (still IConnection)
  private IChannel? _channel; // NEW (was IModel)

  private readonly SemaphoreSlim _connectLock = new(1, 1); // NEW

  public RabbitMqPublisher(RabbitMqOptions options, ILogger<RabbitMqPublisher> logger)
  {
    _options = options;
    _logger = logger;
  }

  public async Task PublishAsync(string routingKey, string messageId, string bodyJson, string? correlationId,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    await EnsureConnectedAsync(ct); // NEW

    var body = Encoding.UTF8.GetBytes(bodyJson);

    // NEW: In v7, create properties with 'new BasicProperties()'
    var props = new BasicProperties
    {
      DeliveryMode = DeliveryModes.Persistent,
      MessageId = messageId,
      Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
      CorrelationId = correlationId
    };

    // NEW: v7 uses BasicPublishAsync
    await _channel!.BasicPublishAsync(
      exchange: _options.Exchange,
      routingKey: routingKey,
      mandatory: false,
      basicProperties: props,
      body: body);
  }

  private async Task EnsureConnectedAsync(CancellationToken ct) // NEW
  {
    if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
      return;

    await _connectLock.WaitAsync(ct);
    try
    {
      if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
        return;

      if (_channel is not null)
      {
        try
        {
          await _channel.CloseAsync();
        }
        catch
        {
        }

        try
        {
          await _channel.DisposeAsync();
        }
        catch
        {
        }

        _channel = null;
      }

      if (_connection is not null)
      {
        try
        {
          await _connection.CloseAsync();
        }
        catch
        {
        }

        try
        {
          await _connection.DisposeAsync();
        }
        catch
        {
        }

        _connection = null;
      }

      var factory = new ConnectionFactory
      {
        HostName = _options.Host,
        Port = _options.Port,
        UserName = _options.User,
        Password = _options.Password,
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        ClientProvidedName = "OrdersApi-OutboxPublisher" // NEW (nice for RabbitMQ UI)
      };

      // NEW: async connection + channel
      _connection = await factory.CreateConnectionAsync(); // :contentReference[oaicite:1]{index=1}
      _channel = await _connection.CreateChannelAsync(); // :contentReference[oaicite:2]{index=2}

      // NEW: async declare exchange
      await _channel.ExchangeDeclareAsync(
        exchange: _options.Exchange,
        type: ExchangeType.Topic,
        durable: true,
        autoDelete: false,
        arguments: null);

      _logger.LogInformation("RabbitMQ connected. Host={Host} Exchange={Exchange}", _options.Host, _options.Exchange);
    }
    finally
    {
      _connectLock.Release();
    }
  }

  public async ValueTask DisposeAsync() // NEW
  {
    try
    {
      if (_channel is not null) await _channel.CloseAsync();
    }
    catch
    {
    }

    try
    {
      if (_connection is not null) await _connection.CloseAsync();
    }
    catch
    {
    }

    try
    {
      if (_channel is not null) await _channel.DisposeAsync();
    }
    catch
    {
    }

    try
    {
      if (_connection is not null) await _connection.DisposeAsync();
    }
    catch
    {
    }

    _connectLock.Dispose();
  }
}
