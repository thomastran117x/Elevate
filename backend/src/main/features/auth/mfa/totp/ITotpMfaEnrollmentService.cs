using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa.totp
{
    public interface ITotpMfaEnrollmentService
    {
        Task<TotpMfaEnrollment?> GetEnrollmentAsync(int userId);
        Task<TotpEnrollmentStartResponse> StartEnrollmentAsync(int userId, string email);
        Task<TotpMfaEnrollment> VerifyEnrollmentAsync(int userId, string code);
        Task<TotpMfaEnrollment> EnableAsync(int userId, string code);
        Task<TotpMfaEnrollment?> DisableAsync(int userId, string code);
        Task RemoveAsync(int userId, string code);

        /// <summary>
        /// Verifies a TOTP code against the user's persisted enrollment.
        /// Used by login step-up. Throws UnauthorizedException on failure.
        /// </summary>
        Task VerifyPersistedCodeAsync(int userId, string code);
    }
}
