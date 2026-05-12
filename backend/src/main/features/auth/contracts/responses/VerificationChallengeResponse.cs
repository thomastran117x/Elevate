namespace backend.main.features.auth.contracts.responses
{
    public sealed class VerificationChallengeResponse
    {
        public required string Challenge { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}
