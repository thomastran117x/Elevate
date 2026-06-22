using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa
{
    public interface IMfaEnrollmentService
    {
        Task<MfaStatusResponse> GetStatusAsync(int userId);
        Task<MfaChallengeResponse> StartEnrollmentAsync(int userId, string phoneNumber);
        Task<MfaStatusResponse> VerifyEnrollmentAsync(int userId, string code, string challenge);
        Task<MfaStatusResponse> DisableAsync(int userId);
    }
}
