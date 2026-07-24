namespace backend.main.shared.providers.messages
{
    public sealed class EmailMessage
    {
        public required EmailMessageType Type
        {
            get; init;
        }
        public required string Email
        {
            get; init;
        }
        public string? Token
        {
            get; init;
        }
        public string? Code
        {
            get; init;
        }
        public string? RecipientName
        {
            get; init;
        }
        public int? EventInvitationId
        {
            get; init;
        }
        public string? EventName
        {
            get; init;
        }
        public string? ClubName
        {
            get; init;
        }
        public string? ActorName
        {
            get; init;
        }
        public DateTime? EventStartsAtUtc
        {
            get; init;
        }
    }
}
