using System.Text;

using Confluent.Kafka;

namespace backend.worker.event_indexer;

public sealed record EventIndexerEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers)
{
    public static EventIndexerEnvelope FromConsumeResult(ConsumeResult<string, string> result)
    {
        var headers = (result.Message.Headers ?? new Headers())
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var valueBytes = group.Last().GetValueBytes();
                    return valueBytes == null ? null : Encoding.UTF8.GetString(valueBytes);
                },
                StringComparer.OrdinalIgnoreCase
            );

        headers.TryGetValue("eventType", out var operation);

        return new EventIndexerEnvelope(
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            result.Message.Key,
            result.Message.Value ?? string.Empty,
            operation,
            headers
        );
    }
}
