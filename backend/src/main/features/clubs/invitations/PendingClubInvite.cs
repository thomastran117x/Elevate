using backend.main.features.clubs.staff;

namespace backend.main.features.clubs.invitations
{
    /// <summary>
    /// The pending club staff invitation as persisted in Redis. The same JSON is stored under the
    /// token key (<c>club:invite:token:{tokenHash}</c>, with a TTL) and as a field in the per-club
    /// index hash (<c>club:invite:club:{clubId}</c>, keyed by <see cref="RecipientUserId"/>).
    /// There is no database row — presence in Redis means "pending", absence means "gone".
    /// </summary>
    public sealed record PendingClubInvite
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
        public ClubStaffRole Role
        {
            get; init;
        }
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
