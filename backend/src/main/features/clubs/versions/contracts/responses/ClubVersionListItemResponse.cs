namespace backend.main.features.clubs.versions.contracts.responses;

public sealed record ClubVersionListItemResponse(
    int VersionNumber,
    string ActionType,
    DateTime CreatedAt,
    int ActorUserId,
    string ActorRole,
    bool RollbackEligible,
    DateTime RollbackExpiresAt,
    int? RollbackSourceVersionNumber,
    IReadOnlyList<ClubVersionFieldChangeResponse> ChangedFields);
