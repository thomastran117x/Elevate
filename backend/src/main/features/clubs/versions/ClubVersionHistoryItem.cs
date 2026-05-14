namespace backend.main.features.clubs.versions;

public sealed record ClubVersionHistoryItem(
    int ClubId,
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<ClubVersionFieldChange> ChangedFields);
