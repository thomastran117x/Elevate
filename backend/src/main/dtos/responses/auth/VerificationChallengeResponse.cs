namespace backend.main.dtos.responses.auth
{
    public sealed class VerificationChallengeResponse
    {
        public required string Challenge { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}
