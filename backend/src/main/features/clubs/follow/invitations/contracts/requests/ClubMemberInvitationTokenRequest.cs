using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.follow.invitations.contracts.requests
{
    public sealed class ClubMemberInvitationTokenRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
