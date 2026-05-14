namespace backend.worker.club_indexer;

public sealed record ClubIndexerEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload,
    string? Operation,
    IReadOnlyDictionary<string, string?> Headers);
