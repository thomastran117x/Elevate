namespace backend.main.features.events.invitations.contracts.responses;

public sealed class EventInvitationLinkResponse
{
    public int Id { get; init; }
    public int EventId { get; init; }
    public string? ShareUrl { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int MaxRedemptions { get; init; }
    public int RedemptionCount { get; init; }
    public bool IsRevoked { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
