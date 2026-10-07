using System.Text.Json;

using backend.main.shared.providers;

using Confluent.Kafka;

namespace backend.worker.media_worker;

public sealed record MediaWorkerDlqMessage(
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string? SourceKey,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers,
    string Payload,
    string Error,
    DateTime FailedAtUtc);

public interface IMediaWorkerDlqPublisher
{
    Task PublishAsync(
        MediaWorkerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaMediaWorkerDlqPublisher : IMediaWorkerDlqPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly MediaWorkerOptions _options;

    public KafkaMediaWorkerDlqPublisher(MediaWorkerOptions options)
    {
        _options = options;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = $"{options.GroupId}-dlq"
        }).Build();
    }

    public async Task PublishAsync(
        MediaWorkerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default)
    {
        var payload = new MediaWorkerDlqMessage(
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
