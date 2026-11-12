namespace backend.main.features.auth.mfa.totp
{
    public interface ITotpMfaEnrollmentRepository
    {
        Task<TotpMfaEnrollment?> GetByUserIdAsync(int userId);
        Task<TotpMfaEnrollment> UpsertAsync(int userId, string encryptedSecret, int keyVersion, DateTime enrolledAtUtc);
        Task<TotpMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled, DateTime? disabledAtUtc);
        Task<bool> RemoveAsync(int userId);

        /// <summary>
        /// The next stored secrets, by ascending user id after <paramref name="afterUserId"/>, that
        /// are not encrypted under the key named by <paramref name="activePrefix"/>.
        /// </summary>
        Task<IReadOnlyList<TotpStoredSecret>> ListSecretsNotUnderKeyAsync(
            string activePrefix, int afterUserId, int batchSize, CancellationToken cancellationToken = default);

        /// <summary>How many stored secrets still need a key other than the active one.</summary>
        Task<int> CountSecretsNotUnderKeyAsync(string activePrefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces a stored secret only if it still holds <paramref name="expectedEncryptedSecret"/>,
        /// so a re-encryption never overwrites a concurrent re-enrollment or removal.
        /// </summary>
        Task<bool> ReplaceSecretAsync(
            int userId,
            string expectedEncryptedSecret,
            string newEncryptedSecret,
            int keyVersion,
            CancellationToken cancellationToken = default);
    }

    /// <summary>A stored secret as the key-rotation sweep reads it: no tracking, only what it needs.</summary>
    public sealed record TotpStoredSecret(int UserId, string EncryptedSecret);
}
