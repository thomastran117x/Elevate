namespace backend.main.features.events.invitations;

public class EventInvitation
{
    public int Id
    {
        get; set;
    }
    public int EventId
    {
        get; set;
    }
    public int? RecipientUserId
    {
        get; set;
    }
    public string? RecipientEmail
    {
        get; set;
    }
    public string? RecipientEmailNormalized
    {
        get; set;
    }
    public EventInvitationSource SourceType { get; set; } = EventInvitationSource.DirectEmail;
    public EventInvitationLifecycleStatus LifecycleStatus { get; set; } = EventInvitationLifecycleStatus.Pending;
    public EventInvitationDeliveryStatus DeliveryStatus { get; set; } = EventInvitationDeliveryStatus.Queued;
    public string? ClaimTokenHash
    {
        get; set;
    }
    public DateTime? ExpiresAt
    {
        get; set;
    }
    public int? EventInvitationLinkId
    {
        get; set;
    }
    public DateTime? AcceptedAtUtc
    {
        get; set;
    }
    public DateTime? DeclinedAtUtc
    {
        get; set;
    }
    public DateTime? RevokedAtUtc
    {
        get; set;
    }
    public int? CreatedByUserId
    {
        get; set;
    }
    public int? AcceptedByUserId
    {
        get; set;
    }
    public int? DeclinedByUserId
    {
        get; set;
    }
    public int? RevokedByUserId
    {
        get; set;
    }
    public string? DeliveryError
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Events Event { get; set; } = null!;
    public EventInvitationLink? EventInvitationLink
    {
        get; set;
    }
}
