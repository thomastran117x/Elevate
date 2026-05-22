namespace backend.main.features.events.invitations.contracts.responses;

public sealed class EventInvitationResolveResponse
{
    public string State { get; init; } = string.Empty;
    public bool RequiresAuthentication { get; init; }
    public bool CanAccept { get; init; }
    public bool CanDecline { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public EventInvitationSummaryEventResponse? Event { get; init; }
}
