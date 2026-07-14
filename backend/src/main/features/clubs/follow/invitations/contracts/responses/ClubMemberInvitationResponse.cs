namespace backend.main.features.clubs.follow.invitations.contracts.responses
{
    /// <summary>A pending club member invitation, as shown to organisers in the members tab.</summary>
    public sealed class ClubMemberInvitationResponse
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
        public DateTime CreatedAtUtc
        {
            get; init;
        }
        public DateTime ExpiresAtUtc
        {
            get; init;
        }
    }
}
