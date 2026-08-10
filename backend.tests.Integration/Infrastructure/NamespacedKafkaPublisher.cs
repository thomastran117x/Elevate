using System.Text.Json;

using backend.main.shared.providers;

using Confluent.Kafka;

namespace backend.tests.Integration.Infrastructure;

public sealed class NamespacedKafkaPublisher : IPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly IntegrationTestEnvironment _environment;
    private readonly TestResourceNamespace _resources;

    public NamespacedKafkaPublisher(
        IntegrationTestEnvironment environment,
        TestResourceNamespace resources)
    {
        _environment = environment;
        _resources = resources;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = environment.KafkaBootstrapServers,
            ClientId = $"backend-tests-{resources.Slot}"
        }).Build();
    }

    public Task PublishAsync<T>(string topic, T message) =>
        _producer.ProduceAsync(
            MapTopic(topic),
            new Message<string, string>
            {
                Value = JsonSerializer.Serialize(message, JsonOptions.Default)
            });

    public ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }

    private string MapTopic(string topic)
    {
        if (topic == _environment.EmailTopic)
            return _resources.EmailTopic;
        if (topic == _environment.SmsTopic)
            return _resources.SmsTopic;
        if (topic == _environment.EmailStatusTopic)
            return _resources.EmailStatusTopic;
        return topic;
    }
}
