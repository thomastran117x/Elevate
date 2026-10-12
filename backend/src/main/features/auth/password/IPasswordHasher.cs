namespace backend.main.features.auth.password
{
    public enum PasswordVerificationResult
    {
        Failed,
        Success,

        /// <summary>
        /// The password is correct, but the stored hash was made with an algorithm or parameters
        /// that are no longer current and should be replaced.
        /// </summary>
        SuccessRehashNeeded,
    }

    /// <summary>
    /// Hashes and verifies passwords across stored formats. New hashes use the configured algorithm;
    /// older formats still verify so that no account has to reset its password when that changes.
    /// </summary>
    public interface IPasswordHasher
    {
        string Hash(string password);

        /// <summary>
        /// Checks <paramref name="password"/> against <paramref name="storedHash"/>. A missing,
        /// unrecognised or malformed hash fails rather than throwing.
        /// </summary>
        PasswordVerificationResult Verify(string password, string? storedHash);

        /// <summary>
        /// A hash of a random value, made with the current parameters. Verifying against it when no
        /// account matched costs the same as a real check, so response time does not reveal which
        /// usernames exist.
        /// </summary>
        string DummyHash
        {
            get;
        }
    }
}
