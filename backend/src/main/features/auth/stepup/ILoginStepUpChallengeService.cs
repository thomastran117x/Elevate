using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.profile;

namespace backend.main.features.auth.stepup
{
    public interface ILoginStepUpChallengeService
    {
        Task<LoginStepUpChallengeResponse> CreateChallengeAsync(
            User user,
            SessionTransport transport,
            bool rememberMe,
            string? returnUrl
        );
        Task<StartLoginStepUpResponse> StartAsync(string challenge, string method);
        Task<AuthenticatedSessionResult> VerifySmsAsync(string challenge, string code);
        Task<AuthenticatedSessionResult?> TryVerifyEmailAsync(string token);
        Task<AuthenticatedSessionResult> VerifyTotpAsync(string challenge, string code);

        /// <summary>
        /// Completes the sign-in by spending one recovery code. The code is spent only once the
        /// account and challenge are known to be good, and the device is not trusted afterwards.
        /// </summary>
        Task<AuthenticatedSessionResult> VerifyRecoveryCodeAsync(string challenge, string code);
    }
}
