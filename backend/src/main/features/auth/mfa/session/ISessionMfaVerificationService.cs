using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// In-session MFA "step-up" used by <c>[RequireMfa]</c> routes. Unlike the
    /// login-time step-up, it does not issue a new session — on success it records
    /// a per-session "MFA verified" marker (keyed by the access token's <c>sid</c>
    /// claim) that lasts for the remainder of the session.
    /// </summary>
    public interface ISessionMfaVerificationService
    {
        Task<SessionMfaOptionsResponse> GetOptionsAsync(int userId, string email);

        Task<SessionMfaStartResponse> StartAsync(int userId, string email, string method);

        Task VerifyAsync(int userId, string email, string sessionId, string method, string code);

        Task<bool> IsSessionVerifiedAsync(string? sessionId);
    }
}
