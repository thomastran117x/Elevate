namespace backend.main.features.clubs.follow.invitations.contracts.responses
{
    /// <summary>Result of accepting/declining a member invitation or redeeming an invite link.</summary>
    public sealed class ClubMemberInvitationDecisionResponse
    {
        public int ClubId
        {
            get; init;
        }
        public bool Accepted
        {
            get; init;
        }
    }
}
