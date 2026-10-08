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
        /// <summary>
        /// The user's step-up generation when verification began. Any completed factor change
        /// advances the generation, which retires every proof minted before it.
        /// </summary>
        public long Generation
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

        /// <summary>Per-user counter advanced by every completed MFA factor change. Never expires.</summary>
        public static string GenerationForUser(int userId) => $"mfa:step-up-generation:{userId}";
    }
}
