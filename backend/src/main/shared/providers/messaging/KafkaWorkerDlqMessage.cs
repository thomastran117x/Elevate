namespace backend.main.shared.providers.messaging
{
    public sealed record KafkaWorkerDlqMessage(
        string SourceTopic,
        int SourcePartition,
        long SourceOffset,
        string? SourceKey,
        IReadOnlyDictionary<string, string?> Headers,
        string Payload,
        string Error,
        DateTime FailedAtUtc);
}
