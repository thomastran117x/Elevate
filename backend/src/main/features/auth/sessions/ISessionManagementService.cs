using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.sessions;

/// <summary>The signed-in owner's view of their sessions, and revocation of them.</summary>
public interface ISessionManagementService
{
    Task<AuthSessionsResponse> ListAsync(int userId, string currentSessionId);

    /// <summary>
    /// Revokes one of the owner's live sessions, which may be the current one.
    /// </summary>
    /// <exception cref="shared.exceptions.http.ResourceNotFoundException">
    /// The id is not one of the owner's live sessions, whether it is unknown, ended, or another
    /// account's.
    /// </exception>
    Task<SessionRevocationResponse> RevokeAsync(int userId, string currentSessionId, string sessionId);

    /// <summary>Revokes every session but the current one.</summary>
    Task<SessionRevocationResponse> RevokeOthersAsync(int userId, string currentSessionId);

    /// <summary>Revokes every session, the current one included.</summary>
    Task<SessionRevocationResponse> RevokeAllAsync(int userId);
}
