namespace backend.main.features.clubs.invitations.contracts.responses
{
    /// <summary>Result of accepting or declining a club staff invitation.</summary>
    public sealed class ClubInvitationDecisionResponse
    {
        public int ClubId
        {
            get; init;
        }
        public string Role { get; init; } = string.Empty;
        public bool Accepted
        {
            get; init;
        }
    }
}
