namespace backend.main.features.clubs.follow.invitations.contracts.responses
{
    public sealed class ClubMemberInvitationClubSummaryResponse
    {
        public int Id
        {
            get; init;
        }
        public string Name { get; init; } = string.Empty;
        public string ClubImage { get; init; } = string.Empty;
    }
}
