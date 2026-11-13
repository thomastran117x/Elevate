using backend.main.shared.responses;

namespace backend.main.features.clubs.moderation.contracts.responses;

public sealed record ClubModerationLogItemResponse(
    int Id,
    int ClubId,
    string Action,
    string ContentType,
    int ContentId,
    string? ContentSnapshot,
    string? Reason,
    string ActorClubRole,
    AuthorInfo Actor,
    AuthorInfo Author,
    DateTime CreatedAt);
