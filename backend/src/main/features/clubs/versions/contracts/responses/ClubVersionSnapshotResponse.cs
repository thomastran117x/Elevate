namespace backend.main.features.clubs.versions.contracts.responses;

public sealed record ClubVersionSnapshotResponse(
    string Name,
    string Description,
    string Clubtype,
    string ClubImage,
    string? Phone,
    string? Email,
    string? WebsiteUrl,
    string? Location,
    int MaxMemberCount,
    bool IsPrivate);
