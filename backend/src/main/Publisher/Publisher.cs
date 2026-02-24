using System.Text;
using System.Text.Json;

using backend.main.Config;
using backend.main.Interfaces;
using backend.main.Utilities;

using RabbitMQ.Client;

namespace backend.main.Queues
{


    public sealed class Publisher : IPublisher, IAsyncDisposable
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;

        public Publisher()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(EnvManager.RabbitConnection),
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
