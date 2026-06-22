using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using Confluent.Kafka;

namespace backend.worker.email_worker;

public interface IEmailDeliveryStatusPublisher
{
    Task PublishAsync(EmailDeliveryStatusMessage message, CancellationToken cancellationToken = default);
}

public sealed class KafkaEmailDeliveryStatusPublisher : IEmailDeliveryStatusPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaEmailDeliveryStatusPublisher(EmailWorkerOptions options)
    {
        _topic = options.StatusTopic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = "email-worker-status-publisher"
        }).Build();
    }

    public async Task PublishAsync(EmailDeliveryStatusMessage message, CancellationToken cancellationToken = default)
    {
        await _producer.ProduceAsync(
            _topic,
            new Message<string, string>
            {
                Value = JsonSerializer.Serialize(message, JsonOptions.Default)
            },
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
