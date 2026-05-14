namespace backend.main.features.clubs.versions.contracts.responses;

public sealed record ClubVersionDetailResponse(
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<ClubVersionFieldChangeResponse> ChangedFields,
    ClubVersionSnapshotResponse Snapshot);
