using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using Confluent.Kafka;

namespace backend.worker.media_worker;

public interface IMediaValidationStatusPublisher
{
    Task PublishAsync(MediaValidationResultMessage message, CancellationToken cancellationToken = default);
}

public sealed class KafkaMediaValidationStatusPublisher : IMediaValidationStatusPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaMediaValidationStatusPublisher(MediaWorkerOptions options)
    {
        _topic = options.StatusTopic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = "media-worker-status-publisher"
        }).Build();
    }

    public async Task PublishAsync(MediaValidationResultMessage message, CancellationToken cancellationToken = default)
    {
        await _producer.ProduceAsync(
            _topic,
            new Message<string, string>
            {
                Key = message.MediaAssetId.ToString(),
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
