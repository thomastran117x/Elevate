using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.sessions;

public sealed class SessionManagementService : ISessionManagementService
{
    private readonly IAuthSessionRepository _sessions;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public SessionManagementService(
        IAuthSessionRepository sessions,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _sessions = sessions;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionsResponse> ListAsync(int userId, string currentSessionId)
    {
        var sessions = await _sessions.ListActiveAsync(userId, _timeProvider.GetUtcNow().UtcDateTime);

        return new AuthSessionsResponse
        {
            Sessions = sessions.Select(session => ToResponse(session, currentSessionId)).ToList(),
        };
    }

    public async Task<SessionRevocationResponse> RevokeAsync(int userId, string currentSessionId, string sessionId)
    {
        // The same 404 for an unknown id, one that already ended, and another account's, so the
        // response says nothing about sessions the caller does not own.
        if (!await _tokenService.RevokeUserRefreshSessionAsync(userId, sessionId, AuthSecurityReason.UserRevokedSession))
            throw new ResourceNotFoundException("Session not found.");

        return new SessionRevocationResponse
        {
            RevokedCount = 1,
            CurrentSessionRevoked = sessionId == currentSessionId,
        };
    }

    public async Task<SessionRevocationResponse> RevokeOthersAsync(int userId, string currentSessionId)
    {
        var revoked = await _tokenService.RevokeOtherRefreshSessionsAsync(
            userId,
            currentSessionId,
            AuthSecurityReason.UserRevokedOtherSessions);

        return new SessionRevocationResponse
        {
            RevokedCount = revoked,
            CurrentSessionRevoked = false,
        };
    }

    public async Task<SessionRevocationResponse> RevokeAllAsync(int userId)
    {
        // Counted from the registry beforehand, for the response only; the sweep itself also ends
        // any session that predates the registry.
        var live = await _sessions.ListActiveAsync(userId, _timeProvider.GetUtcNow().UtcDateTime);
        await _tokenService.RevokeAllRefreshSessionsAsync(userId, AuthSecurityReason.UserRevokedAllSessions);

        return new SessionRevocationResponse
        {
            RevokedCount = live.Count,
            CurrentSessionRevoked = true,
        };
    }

    private static AuthSessionResponse ToResponse(AuthSession session, string currentSessionId) => new()
    {
        Id = session.Id,
        Current = session.Id == currentSessionId,
        Transport = session.Transport.UsesBrowserCookies()
            ? SessionTransportResolver.BrowserValue
            : SessionTransportResolver.ApiValue,
        Client = AuthSecurityWireNames.Of(session.Client),
        DeviceClass = AuthSecurityWireNames.Of(session.DeviceClass),
        Network = session.Network,
        RememberMe = session.RememberMe,
        CreatedAt = session.CreatedAt,
        LastSeenAt = session.LastSeenAt,
        ExpiresAt = session.ExpiresAt,
    };
}
