namespace backend.main.features.clubs.follow.invitations.contracts.responses
{
    /// <summary>A shareable club member invite link, as shown to organisers in the members tab.</summary>
    public sealed class ClubInvitationLinkResponse
    {
        public int Id
        {
            get; init;
        }
        public int ClubId
        {
            get; init;
        }

        /// <summary>The frontend URL to share. Only populated at creation time (holds the raw token).</summary>
        public string? ShareUrl
        {
            get; init;
        }
        public DateTime ExpiresAt
        {
            get; init;
        }

        /// <summary>Redemption cap, or <c>null</c> for unlimited.</summary>
        public int? MaxRedemptions
        {
            get; init;
        }
        public int RedemptionCount
        {
            get; init;
        }
        public bool IsRevoked
        {
            get; init;
        }
        public DateTime? RevokedAtUtc
        {
            get; init;
        }
        public DateTime CreatedAt
        {
            get; init;
        }
        public DateTime UpdatedAt
        {
            get; init;
        }
    }
}
