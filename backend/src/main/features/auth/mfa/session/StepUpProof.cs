namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// A recent second-factor verification, stored in Redis for one refresh session. It is valid
    /// only for the user, session, and auth version that produced it, and only until
    /// <see cref="StepUpOptions.ProofLifetime"/> has passed since <see cref="VerifiedAtUtc"/>.
    /// </summary>
    public sealed class StepUpProof
    {
        public int UserId
        {
            get; set;
        }
        public required string SessionId
        {
            get; set;
        }
        public int AuthVersion
        {
            get; set;
        }
        public required string Method
        {
            get; set;
        }
        public DateTime VerifiedAtUtc
        {
            get; set;
        }
    }

    /// <summary>
    /// Redis keys for <see cref="StepUpProof"/>. Kept static so session revocation in
    /// <c>TokenService</c> can clear a proof without depending on the step-up service.
    /// </summary>
    public static class StepUpProofKeys
    {
        public static string ForSession(string sessionId) => $"mfa:step-up-proof:{sessionId}";
    }
}
