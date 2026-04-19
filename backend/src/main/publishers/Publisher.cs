using System.Text;
using System.Text.Json;

using backend.main.configurations.environment;
using backend.main.publishers.interfaces;

using RabbitMQ.Client;

namespace backend.main.publishers.implementation
{
    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public sealed class Publisher : IPublisher, IAsyncDisposable
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;

        public Publisher()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(EnvironmentSetting.RabbitConnection),
                AutomaticRecoveryEnabled = true
            };

            _connection = factory.CreateConnectionAsync().Result;
            _channel = _connection.CreateChannelAsync().Result;

            _channel.QueueDeclareAsync(
                queue: "eventxperience-email",
                durable: true,
                exclusive: false,
                autoDelete: false
            ).GetAwaiter().GetResult();

            _channel.QueueDeclareAsync(
                queue: "clubpost-es-index-dlq",
                durable: true,
                exclusive: false,
                autoDelete: false
            ).GetAwaiter().GetResult();

            _channel.QueueDeclareAsync(
                queue: "clubpost-es-index",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = "clubpost-es-index-dlq"
                }
            ).GetAwaiter().GetResult();

            _channel.QueueDeclareAsync(
                queue: "event-es-index-dlq",
                durable: true,
                exclusive: false,
                autoDelete: false
            ).GetAwaiter().GetResult();

            _channel.QueueDeclareAsync(
                queue: "event-es-index",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = "event-es-index-dlq"
                }
            ).GetAwaiter().GetResult();
        }

        public async Task PublishAsync<T>(string queue, T message)
        {
            var body = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(message, JsonOptions.Default)
            );

            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: queue,
                body: body
            );
        }

        public async ValueTask DisposeAsync()
        {
            await _channel.CloseAsync();
            await _connection.CloseAsync();
        }
    }
}
