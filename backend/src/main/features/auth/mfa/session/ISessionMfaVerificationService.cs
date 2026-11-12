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
        /// The step-up generation and factor state now. A verification captures them before it
        /// checks anything, so a factor change that completes meanwhile makes its proof fail to
        /// store instead of outliving the change.
        /// </summary>
        Task<StepUpSnapshot> CaptureSnapshotAsync(int userId);

        /// <summary>
        /// Records the proof for a passkey assertion the passkey service has already verified,
        /// against the state captured when its ceremony started.
        /// </summary>
        Task RecordPasskeyProofAsync(int userId, string sessionId, int authVersion, StepUpSnapshot snapshot);

        /// <summary>
        /// True only when the session holds an unexpired proof for this exact user and
        /// auth version. Reading a proof never extends it.
        /// </summary>
        Task<bool> HasFreshProofAsync(int userId, string? sessionId, int authVersion);

        Task ClearSessionProofAsync(string sessionId);

        /// <summary>
        /// Retires the proof on every refresh session the user holds, including one a concurrent
        /// verification is about to write, by advancing the user's step-up generation. Throws
        /// when the cache cannot be updated rather than silently leaving proofs usable.
        /// </summary>
        Task ClearUserProofsAsync(int userId);
    }
}
