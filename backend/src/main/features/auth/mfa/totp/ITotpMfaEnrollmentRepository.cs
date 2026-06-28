namespace backend.main.features.auth.mfa.totp
{
    public interface ITotpMfaEnrollmentRepository
    {
        Task<TotpMfaEnrollment?> GetByUserIdAsync(int userId);
        Task<TotpMfaEnrollment> UpsertAsync(int userId, string encryptedSecret, int keyVersion, DateTime enrolledAtUtc);
        Task<TotpMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled, DateTime? disabledAtUtc);
        Task<bool> RemoveAsync(int userId);
    }
}
