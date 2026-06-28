namespace backend.main.features.auth.mfa
{
    public interface IMfaEnrollmentRepository
    {
        Task<SmsMfaEnrollment?> GetByUserIdAsync(int userId);
        Task<SmsMfaEnrollment> UpsertVerifiedPhoneAsync(
            int userId,
            string phoneNumber,
            DateTime verifiedAtUtc
        );
        Task<SmsMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled);
        Task<bool> RemoveAsync(int userId);
    }
}
