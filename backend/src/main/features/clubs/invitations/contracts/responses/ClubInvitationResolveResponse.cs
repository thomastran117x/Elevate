namespace backend.main.features.clubs.invitations.contracts.responses
{
    /// <summary>Drives the club invite accept page: what state the token is in and what actions are allowed.</summary>
    public sealed class ClubInvitationResolveResponse
    {
        public string State { get; init; } = string.Empty;
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
        public string? Role
        {
            get; init;
        }
        public DateTime? ExpiresAtUtc
        {
            get; init;
        }
        public ClubInvitationClubSummaryResponse? Club
        {
            get; init;
        }
    }
}
