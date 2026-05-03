using System.Text;

using Confluent.Kafka;

namespace backend.worker.clubpost_indexer;

public sealed record ClubPostIndexerEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload,
    IReadOnlyDictionary<string, string?> Headers)
{
    public static ClubPostIndexerEnvelope FromConsumeResult(ConsumeResult<string, string> result)
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

        return new ClubPostIndexerEnvelope(
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            result.Message.Key,
            result.Message.Value ?? string.Empty,
            headers
        );
    }
}
