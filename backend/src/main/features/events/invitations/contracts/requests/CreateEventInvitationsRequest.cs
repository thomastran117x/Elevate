using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.invitations.contracts.requests;

public sealed class CreateEventInvitationsRequest
{
    [MaxLength(100)]
    public List<int>? UserIds { get; set; }

    [MaxLength(100)]
    public List<string>? Emails { get; set; }

    public DateTime? ExpiresAt { get; set; }
}
