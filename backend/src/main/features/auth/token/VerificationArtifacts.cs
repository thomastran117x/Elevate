namespace backend.main.features.auth.token
{
    public sealed class VerificationArtifacts
    {
        public required string LinkToken { get; init; }
        public required VerificationOtpChallenge OtpChallenge { get; init; }
        public required VerificationPurpose Purpose { get; init; }
    }
}
