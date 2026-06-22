using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messaging;

using Confluent.Kafka;

namespace backend.worker.sms_worker;

public interface ISmsWorkerDlqPublisher
{
    Task PublishAsync(
        KafkaMessageEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default);
}

public sealed class KafkaSmsWorkerDlqPublisher : ISmsWorkerDlqPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly SmsWorkerOptions _options;

    public KafkaSmsWorkerDlqPublisher(SmsWorkerOptions options)
    {
        _options = options;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = $"{options.GroupId}-dlq"
        }).Build();
    }

    public async Task PublishAsync(
        KafkaMessageEnvelope envelope,
        string error,
        CancellationToken cancellationToken = default)
    {
        var payload = new KafkaWorkerDlqMessage(
            envelope.Topic,
            envelope.Partition,
            envelope.Offset,
            envelope.Key,
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
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
