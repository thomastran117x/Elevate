using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.invitations.contracts.requests
{
    public sealed class ClubInvitationTokenRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
