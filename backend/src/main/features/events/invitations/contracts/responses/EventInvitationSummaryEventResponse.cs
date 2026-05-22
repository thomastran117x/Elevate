using backend.main.features.events.contracts.responses;

namespace backend.main.features.events.invitations.contracts.responses;

public sealed class EventInvitationSummaryEventResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public bool IsPrivate { get; init; }
    public int RegisterCost { get; init; }
    public int MaxParticipants { get; init; }
    public int RegistrationCount { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public List<string> ImageUrls { get; init; } = [];
    public EventHostClubResponse? Club { get; init; }
}
