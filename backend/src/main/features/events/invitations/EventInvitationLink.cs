namespace backend.main.features.events.invitations;

public class EventInvitationLink
{
    public int Id
    {
        get; set;
    }
    public int EventId
    {
        get; set;
    }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt
    {
        get; set;
    }
    public int MaxRedemptions
    {
        get; set;
    }
    public int RedemptionCount
    {
        get; set;
    }
    public int CreatedByUserId
    {
        get; set;
    }
    public int? RevokedByUserId
    {
        get; set;
    }
    public DateTime? RevokedAtUtc
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Events Event { get; set; } = null!;
    public ICollection<EventInvitation> Invitations { get; set; } = new List<EventInvitation>();
}
