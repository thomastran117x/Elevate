namespace backend.main.features.auth.token
{
    public sealed class VerificationOtpChallenge
    {
        public required string Code { get; init; }
        public required string Challenge { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}
