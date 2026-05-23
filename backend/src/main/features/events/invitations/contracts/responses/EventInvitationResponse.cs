namespace backend.main.features.events.invitations.contracts.responses;

public sealed class EventInvitationResponse
{
    public int Id
    {
        get; init;
    }
    public int EventId
    {
        get; init;
    }
    public int? RecipientUserId
    {
        get; init;
    }
    public string? RecipientEmail
    {
        get; init;
    }
    public string SourceType { get; init; } = string.Empty;
    public string LifecycleStatus { get; init; } = string.Empty;
    public string EffectiveStatus { get; init; } = string.Empty;
    public string DeliveryStatus { get; init; } = string.Empty;
    public DateTime? ExpiresAt
    {
        get; init;
    }
    public DateTime? AcceptedAtUtc
    {
        get; init;
    }
    public DateTime? DeclinedAtUtc
    {
        get; init;
    }
    public DateTime? RevokedAtUtc
    {
        get; init;
    }
    public int? EventInvitationLinkId
    {
        get; init;
    }
    public string? DeliveryError
    {
        get; init;
    }
    public DateTime CreatedAt
    {
        get; init;
    }
    public DateTime UpdatedAt
    {
        get; init;
    }
    public EventInvitationSummaryEventResponse? Event
    {
        get; init;
    }
}
