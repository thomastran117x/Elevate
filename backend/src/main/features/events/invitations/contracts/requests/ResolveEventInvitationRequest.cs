using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.invitations.contracts.requests;

public sealed class ResolveEventInvitationRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}
