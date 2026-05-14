namespace backend.main.features.events.versions;

public sealed class EventVersionSnapshot
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Location { get; init; }
    public bool IsPrivate { get; init; }
    public int MaxParticipants { get; init; }
    public int RegisterCost { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int ClubId { get; init; }
    public EventCategory Category { get; init; }
    public string? VenueName { get; init; }
    public string? City { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public List<string> Tags { get; init; } = new();
}
