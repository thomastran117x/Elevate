using System.Text.Json;

using backend.main.shared.providers;

using Confluent.Kafka;

namespace backend.worker.club_indexer;

public sealed record ClubIndexerDlqMessage(
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    string? SourceKey,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers,
    string Payload,
    string Error,
    DateTime FailedAtUtc);

public interface IClubIndexerDlqPublisher
{
    Task PublishAsync(
        ClubIndexerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaClubIndexerDlqPublisher : IClubIndexerDlqPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ClubIndexerOptions _options;

    public KafkaClubIndexerDlqPublisher(ClubIndexerOptions options)
    {
        _options = options;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = $"{options.GroupId}-dlq"
        }).Build();
    }

    public async Task PublishAsync(
        ClubIndexerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default)
    {
        var payload = new ClubIndexerDlqMessage(
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
