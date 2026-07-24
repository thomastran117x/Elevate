namespace backend.main.features.clubs.invitations.contracts.responses
{
    public sealed class ClubInvitationClubSummaryResponse
    {
        public int Id
        {
            get; init;
        }
        public string Name { get; init; } = string.Empty;
        public string ClubImage { get; init; } = string.Empty;
    }
}
