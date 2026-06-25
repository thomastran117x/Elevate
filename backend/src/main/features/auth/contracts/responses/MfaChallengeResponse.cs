namespace backend.main.features.auth.contracts.responses
{
    public sealed class MfaChallengeResponse
    {
        public required string Challenge
        {
            get; init;
        }

        public DateTime ExpiresAtUtc
        {
            get; init;
        }

        public required string Channel
        {
            get; init;
        }

        public required string MaskedDestination
        {
            get; init;
        }
    }
}
