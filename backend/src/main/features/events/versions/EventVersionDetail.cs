namespace backend.main.features.events.versions;

public sealed record EventVersionDetail(
    int EventId,
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<EventVersionFieldChange> ChangedFields,
    EventVersionSnapshot Snapshot);
