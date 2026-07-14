namespace backend.main.features.clubs.follow.invitations
{
    /// <summary>
    /// A shareable, DB-backed club member invite link. Anyone with the raw token can redeem it to
    /// join the club until it expires, is revoked, or (optionally) reaches its redemption cap.
    /// Unlike a specific member invitation this is not bound to a recipient and sends no email.
    /// Mirrors <c>EventInvitationLink</c>.
    /// </summary>
    public class ClubInvitationLink
    {
        public int Id
        {
            get; set;
        }
        public int ClubId
        {
            get; set;
        }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt
        {
            get; set;
        }

        /// <summary>Maximum number of redemptions, or <c>null</c> for unlimited (until expiry/revoke).</summary>
        public int? MaxRedemptions
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

        public Club Club { get; set; } = null!;
    }
}
