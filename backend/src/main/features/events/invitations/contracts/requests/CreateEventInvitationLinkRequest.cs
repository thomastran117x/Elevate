using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.invitations.contracts.requests;

public sealed class CreateEventInvitationLinkRequest
{
    [Range(1, 10000)]
    public int MaxRedemptions { get; set; } = 1;

    public DateTime ExpiresAt
    {
        get; set;
    }
}
