using System.Text.Json;

using backend.main.shared.providers;

using Confluent.Kafka;

namespace backend.tests.Integration.Infrastructure;

public sealed class KafkaTopicProbe
{
    private readonly string _bootstrapServers;
    private readonly Dictionary<string, Offset> _offsets = new(StringComparer.Ordinal);

    public KafkaTopicProbe(string bootstrapServers)
    {
        _bootstrapServers = bootstrapServers;
    }

    public Task MarkBoundaryAsync(string topic) =>
        Task.Run(() =>
        {
            using var consumer = BuildConsumer();
            var watermark = consumer.QueryWatermarkOffsets(
                new TopicPartition(topic, new Partition(0)),
                TimeSpan.FromSeconds(5));
            _offsets[topic] = watermark.High;
        });

    public async Task MarkBoundaryAsync(params string[] topics)
    {
        foreach (var topic in topics)
            await MarkBoundaryAsync(topic);
    }

    public Task<T> WaitForAsync<T>(
        string topic,
        Func<T, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(10));
        var consumer = BuildConsumer();
        try
        {
            var startOffset = _offsets.TryGetValue(topic, out var offset)
                ? offset
                : Offset.Beginning;
            consumer.Assign(new TopicPartitionOffset(topic, new Partition(0), startOffset));

            while (DateTime.UtcNow < deadline)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                if (result?.Message?.Value is null)
                    continue;

                _offsets[topic] = result.Offset + 1;

                var message = JsonSerializer.Deserialize<T>(result.Message.Value, JsonOptions.Default);
                if (message is not null && predicate(message))
                    return Task.FromResult(message);
            }
        }
        finally
        {
            consumer.Close();
            consumer.Dispose();
        }

        throw new TimeoutException($"Timed out waiting for a matching message on Kafka topic '{topic}'.");
    }

    public Task<IReadOnlyList<T>> ReadNewAsync<T>(
        string topic,
        TimeSpan? timeout = null)
    {
        var messages = new List<T>();
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(1));
        var consumer = BuildConsumer();
        try
        {
            var startOffset = _offsets.TryGetValue(topic, out var offset)
                ? offset
                : Offset.Beginning;
            consumer.Assign(new TopicPartitionOffset(topic, new Partition(0), startOffset));

            while (DateTime.UtcNow < deadline)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(200));
                if (result?.Message?.Value is null)
                {
                    if (messages.Count > 0)
                        break;

                    continue;
                }

                _offsets[topic] = result.Offset + 1;

                var message = JsonSerializer.Deserialize<T>(result.Message.Value, JsonOptions.Default);
                if (message is not null)
                    messages.Add(message);
            }
        }
        finally
        {
            consumer.Close();
            consumer.Dispose();
        }

        return Task.FromResult<IReadOnlyList<T>>(messages);
    }

    private IConsumer<Ignore, string> BuildConsumer()
    {
        return new ConsumerBuilder<Ignore, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = $"backend-tests-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
    }
}
