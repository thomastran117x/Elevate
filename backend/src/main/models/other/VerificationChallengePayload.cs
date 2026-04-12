namespace backend.main.models.other
{
    internal sealed class VerificationChallengePayload
    {
        public required VerificationPurpose Purpose { get; init; }
        public required string Email { get; init; }
        public string? Password { get; init; }
        public string? Usertype { get; init; }
        public required string OtpProof { get; init; }
        public required string Nonce { get; init; }
        public DateTime ExpiresAtUtc { get; init; }
    }
}
