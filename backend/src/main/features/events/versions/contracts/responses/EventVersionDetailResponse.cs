namespace backend.main.features.events.versions.contracts.responses;

public sealed record EventVersionDetailResponse(
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<EventVersionFieldChangeResponse> ChangedFields,
    EventVersionSnapshotResponse Snapshot);
