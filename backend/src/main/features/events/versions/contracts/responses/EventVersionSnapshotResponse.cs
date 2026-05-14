using backend.main.features.events;

namespace backend.main.features.events.versions.contracts.responses;

public sealed record EventVersionSnapshotResponse(
    string Name,
    string Description,
    string Location,
    bool IsPrivate,
    int MaxParticipants,
    int RegisterCost,
    DateTime StartTime,
    DateTime? EndTime,
    int ClubId,
    EventCategory Category,
    string? VenueName,
    string? City,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Tags);
