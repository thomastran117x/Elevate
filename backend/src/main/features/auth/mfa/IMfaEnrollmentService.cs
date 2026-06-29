using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa
{
    public interface IMfaEnrollmentService
    {
        Task<MfaChallengeResponse> StartEnrollmentAsync(int userId, string phoneNumber);
        Task<MfaChallengeResponse> StartEnableAsync(int userId);
        Task<SmsMfaEnrollment> VerifyEnrollmentAsync(int userId, string code, string challenge);
        Task<SmsMfaEnrollment?> DisableAsync(int userId);
        Task RemoveAsync(int userId);
    }
}
