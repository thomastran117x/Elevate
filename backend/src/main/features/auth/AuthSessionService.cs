using backend.main.application.security;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth
{
    public sealed class AuthSessionService : IAuthSessionService
    {
        private readonly ITokenService _tokenService;
        private readonly ClientRequestInfo _requestInfo;

        public AuthSessionService(ITokenService tokenService, ClientRequestInfo requestInfo)
        {
            _tokenService = tokenService;
            _requestInfo = requestInfo;
        }

        public async Task<UserToken> IssueAsync(
            User user,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        )
        {
            try
            {
                user.Usertype = AuthRoles.NormalizeStored(user.Usertype);
                // Mint the refresh session first so the access token can carry its
                // session id (sid) claim. On login the session id is generated here;
                // on refresh the existing session id is passed through and reused, so
                // sid stays stable across access-token rotations within one session.
                var refreshToken = await _tokenService.GenerateRefreshToken(
                    user.Id,
                    _requestInfo,
                    transport,
                    sessionId,
                    rememberMe
                );
                var accessToken = _tokenService.GenerateAccessToken(user, refreshToken.SessionId);
                var authToken = new Token(
                    accessToken.Value,
                    accessToken.ExpiresAtUtc,
                    refreshToken.Value,
                    refreshToken.SessionBindingToken,
                    refreshToken.Lifetime,
                    refreshToken.Transport
                );

                return new UserToken(authToken, user);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[AuthSessionService] IssueAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }
    }
}
