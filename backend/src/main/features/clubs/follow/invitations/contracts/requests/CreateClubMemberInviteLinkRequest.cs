using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.follow.invitations.contracts.requests
{
    public sealed class CreateClubMemberInviteLinkRequest
    {
        /// <summary>When the link stops working. Must be in the future.</summary>
        [Required]
        public DateTime ExpiresAt
        {
            get; set;
        }

        /// <summary>Optional cap on how many people may join through the link. Omit for unlimited.</summary>
        [Range(1, 10000)]
        public int? MaxRedemptions
        {
            get; set;
        }
    }
}
