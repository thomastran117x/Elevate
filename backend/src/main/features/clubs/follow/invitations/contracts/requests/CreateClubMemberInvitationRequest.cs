using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.follow.invitations.contracts.requests
{
    public sealed class CreateClubMemberInvitationRequest
    {
        /// <summary>A registered user's username or email address.</summary>
        [RequestText(RequestTextKind.Structured)]
        [Required]
        [MaxLength(320)]
        public string Identifier { get; set; } = string.Empty;
    }
}
