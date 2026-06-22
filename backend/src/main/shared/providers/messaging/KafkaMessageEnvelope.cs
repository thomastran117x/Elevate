using System.Text;

using Confluent.Kafka;

namespace backend.main.shared.providers.messaging
{
    public sealed record KafkaMessageEnvelope(
        string Topic,
        int Partition,
        long Offset,
        string? Key,
        string Payload,
        IReadOnlyDictionary<string, string?> Headers)
    {
        public static KafkaMessageEnvelope FromConsumeResult(ConsumeResult<string, string> result)
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

            return new KafkaMessageEnvelope(
                result.Topic,
                result.Partition.Value,
                result.Offset.Value,
                result.Message.Key,
                result.Message.Value ?? string.Empty,
                headers
            );
        }
    }
}
