namespace backend.main.features.auth.mfa.recovery
{
    /// <summary>
    /// One single-use recovery code. Only its hash is stored; the plaintext exists once, in the
    /// response that generated it. A user has at most one set: regeneration replaces every row.
    /// </summary>
    public sealed class RecoveryCode
    {
        public int Id
        {
            get; set;
        }

        public int UserId
        {
            get; set;
        }

        /// <summary>
        /// Keyed HMAC of the normalized code, used only to find the row to verify, so a check runs
        /// one Argon2id derivation rather than one per code in the set.
        /// </summary>
        public required string Selector
        {
            get; set;
        }

        /// <summary>Argon2id PHC string, derived with the recovery-code pepper as its secret.</summary>
        public required string CodeHash
        {
            get; set;
        }

        public DateTime CreatedAtUtc
        {
            get; set;
        }

        public DateTime? UsedAtUtc
        {
            get; set;
        }
    }
}
