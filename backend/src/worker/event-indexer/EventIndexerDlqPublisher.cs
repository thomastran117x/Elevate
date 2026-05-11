using System.Text.Json;

using backend.main.shared.providers;

using Confluent.Kafka;

namespace backend.worker.event_indexer;

public sealed record EventIndexerDlqMessage(
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string? SourceKey,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers,
    string Payload,
    string Error,
    DateTime FailedAtUtc);

public interface IEventIndexerDlqPublisher
{
    Task PublishAsync(
        EventIndexerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaEventIndexerDlqPublisher : IEventIndexerDlqPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly EventIndexerOptions _options;

    public KafkaEventIndexerDlqPublisher(EventIndexerOptions options)
    {
        _options = options;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = $"{options.GroupId}-dlq"
        }).Build();
    }

    public async Task PublishAsync(
        EventIndexerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default)
    {
        var payload = new EventIndexerDlqMessage(
            envelope.Topic,
            envelope.Partition,
            envelope.Offset,
            envelope.Key,
            envelope.Operation,
            envelope.Headers,
            envelope.Payload,
            error,
            DateTime.UtcNow
        );

        await _producer.ProduceAsync(
            _options.DlqTopic,
            new Message<string, string>
            {
                Key = envelope.Key ?? envelope.Offset.ToString(),
                Value = JsonSerializer.Serialize(payload, JsonOptions.Default)
            },
            cancellationToken
        );
    }

    public ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
