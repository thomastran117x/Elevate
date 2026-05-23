namespace backend.worker.event_indexer;

public sealed record EventIndexerEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers);
