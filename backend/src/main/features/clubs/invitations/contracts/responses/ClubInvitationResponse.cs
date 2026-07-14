namespace backend.main.features.clubs.invitations.contracts.responses
{
    /// <summary>A pending club staff invitation, as shown to the owner in the staff tab.</summary>
    public sealed class ClubInvitationResponse
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
        public string Role { get; init; } = string.Empty;
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
