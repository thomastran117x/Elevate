namespace backend.main.features.clubs.follow.invitations
{
    /// <summary>
    /// A pending club member (follower) invitation as persisted in Redis. The same JSON is stored
    /// under the token key (<c>club:memberinvite:token:{tokenHash}</c>, with a TTL) and as a field in
    /// the per-club index hash (<c>club:memberinvite:club:{clubId}</c>, keyed by <see cref="RecipientUserId"/>).
    /// There is no database row — presence in Redis means "pending", absence means "gone". Unlike a
    /// staff invitation this carries no role: accepting simply grants membership.
    /// </summary>
    public sealed record PendingClubMemberInvite
    {
        public int ClubId
        {
            get; init;
        }
        public int RecipientUserId
        {
            get; init;
        }
        public string RecipientEmail { get; init; } = string.Empty;
        public int CreatedByUserId
        {
            get; init;
        }
        public DateTime CreatedAtUtc
        {
            get; init;
        }
        public DateTime ExpiresAtUtc
        {
            get; init;
        }

        /// <summary>SHA-256 hash of the opaque token; lets revoke find the token key from the index.</summary>
        public string TokenHash { get; init; } = string.Empty;
    }
}
