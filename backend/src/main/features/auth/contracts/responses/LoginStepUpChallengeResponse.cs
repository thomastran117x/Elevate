namespace backend.main.features.auth.contracts.responses
{
    public sealed class LoginStepUpChallengeResponse
    {
        public required string Challenge
        {
            get; set;
        }
        public DateTime ExpiresAtUtc
        {
            get; set;
        }
        public required string[] AvailableMethods
        {
            get; set;
        }
        public string? MaskedPhone
        {
            get; set;
        }
        public required string MaskedEmail
        {
            get; set;
        }
    }
}
