namespace backend.worker.clubpost_indexer;

public sealed record ClubPostIndexerEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers);
