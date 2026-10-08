using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// In-session MFA "step-up" used by <c>[RequireMfa]</c> routes. Unlike the
    /// login-time step-up, it does not issue a new session — on success it records
    /// a <see cref="StepUpProof"/> bound to the user, the access token's <c>sid</c>
    /// claim, and its auth version. The proof expires a fixed
    /// <see cref="StepUpOptions.ProofLifetime"/> after verification and never slides.
    /// </summary>
    public interface ISessionMfaVerificationService
    {
        Task<SessionMfaOptionsResponse> GetOptionsAsync(int userId, string email);

        Task<SessionMfaStartResponse> StartAsync(int userId, string email, string method);

        Task VerifyAsync(
            int userId,
            string email,
            string sessionId,
            int authVersion,
            string method,
            string code
        );

        /// <summary>
        /// True only when the session holds an unexpired proof for this exact user and
        /// auth version. Reading a proof never extends it.
        /// </summary>
        Task<bool> HasFreshProofAsync(int userId, string? sessionId, int authVersion);

        Task ClearSessionProofAsync(string sessionId);

        /// <summary>
        /// Clears the proof on every refresh session the user holds, so a completed
        /// sensitive change cannot be followed by another on the same verification.
        /// </summary>
        Task ClearUserProofsAsync(int userId);
    }
}
