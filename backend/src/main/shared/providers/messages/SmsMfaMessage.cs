namespace backend.main.shared.providers.messages
{
    public sealed class SmsMfaMessage
    {
        public required string PhoneNumber
        {
            get; init;
        }
        public required string Code
        {
            get; init;
        }
        public required string Challenge
        {
            get; init;
        }
        public required string Purpose
        {
            get; init;
        }
        public DateTime ExpiresAtUtc
        {
            get; init;
        }
    }
}
