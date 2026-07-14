namespace backend.main.features.clubs.follow.invitations.contracts.responses
{
    /// <summary>Drives the member invite accept page: the token's state, its source, and allowed actions.</summary>
    public sealed class ClubMemberInvitationResolveResponse
    {
        public string State { get; init; } = string.Empty;

        /// <summary>"DirectInvite" or "Link" — tells the UI which accept endpoint to call.</summary>
        public string Source { get; init; } = string.Empty;
        public bool RequiresAuthentication
        {
            get; init;
        }
        public bool CanAccept
        {
            get; init;
        }
        public bool CanDecline
        {
            get; init;
        }
        public DateTime? ExpiresAtUtc
        {
            get; init;
        }
        public ClubMemberInvitationClubSummaryResponse? Club
        {
            get; init;
        }
    }
}
