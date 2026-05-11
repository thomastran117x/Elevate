using System.Text.Json;
using System.Text.Json.Serialization;

using backend.main.application.environment;
using backend.main.publishers.interfaces;

using Confluent.Kafka;

namespace backend.main.shared.providers
{
    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public sealed class Publisher : IPublisher, IAsyncDisposable
    {
        private readonly IProducer<string, string> _producer;

        public Publisher()
        {
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = EnvironmentSetting.KafkaBootstrapServers,
                ClientId = "backend-publisher"
            }).Build();
        }

        public async Task PublishAsync<T>(string topic, T message)
        {
            await _producer.ProduceAsync(
                topic,
                new Message<string, string>
                {
                    Value = JsonSerializer.Serialize(message, JsonOptions.Default)
                }
            );
        }

        public ValueTask DisposeAsync()
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
