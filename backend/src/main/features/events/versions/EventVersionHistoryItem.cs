namespace backend.main.features.events.versions;

public sealed record EventVersionHistoryItem(
    int EventId,
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<EventVersionFieldChange> ChangedFields);
