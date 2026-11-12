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
        /// <summary>
        /// The committed SMS/TOTP enrollment, recovery-code set, and passkey set in the database
        /// when verification began.
        /// Every committed factor change rewrites it, so the change itself retires older proofs
        /// even if the cache-side generation could not be advanced.
        /// </summary>
        public string FactorState { get; set; } = string.Empty;
    }

    /// <summary>
    /// Formats <see cref="StepUpProof.FactorState"/> from the committed enrollments. Pending
    /// enrollments live in the cache, so only completed factor changes move it; each of them
    /// rewrites UpdatedAt or deletes the row. A recovery-code set is identified by its generation
    /// time, which every code in it shares and every regeneration replaces. Spending a code does
    /// not move it, since that is a verification rather than a factor change.
    /// </summary>
    public static class StepUpFactorState
    {
        /// <param name="passkeys">
        /// The account's passkey count and newest registration: adding a passkey moves the newest
        /// time and removing one moves the count, while renaming moves neither.
        /// </param>
        public static string From(
            SmsMfaEnrollment? sms,
            totp.TotpMfaEnrollment? totp,
            DateTime? recoveryCodesGeneratedAtUtc,
            passkeys.PasskeyFactorSummary? passkeys = null) =>
            string.Join(
                "|",
                $"sms:{sms?.UpdatedAt.ToUniversalTime().Ticks ?? 0}",
                $"totp:{totp?.UpdatedAtUtc.ToUniversalTime().Ticks ?? 0}",
                $"recovery:{recoveryCodesGeneratedAtUtc?.ToUniversalTime().Ticks ?? 0}",
                $"passkeys:{passkeys?.Count ?? 0}:{passkeys?.NewestCreatedAtUtc?.ToUniversalTime().Ticks ?? 0}"
            );
    }

    /// <summary>
    /// What a verification captured before checking anything: the user's step-up generation and
    /// factor state. The proof it produces carries both, so a factor change completing during the
    /// verification retires the proof.
    /// </summary>
    public sealed record StepUpSnapshot(long Generation, string FactorState);

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
