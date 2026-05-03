using System.Text.Json;

using backend.main.publishers.implementation;

using Confluent.Kafka;

namespace backend.worker.event_indexer;

public interface IClubPostIndexerDlqPublisher
{
    Task PublishAsync(
        EventIndexerEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaClubPostIndexerDlqPublisher : IClubPostIndexerDlqPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ClubPostIndexerOptions _options;

    public KafkaClubPostIndexerDlqPublisher(ClubPostIndexerOptions options)
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
