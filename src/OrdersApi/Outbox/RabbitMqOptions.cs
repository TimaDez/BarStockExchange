namespace OrdersApi.Outbox;

// NEW
public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "app";
    public string Password { get; set; } = "app";
    public string Exchange { get; set; } = "bar.events";
}
