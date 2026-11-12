using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.utilities;

namespace backend.main.features.auth
{
    /// <summary>
    /// Turns an issued session into its response. A browser session's refresh and binding tokens
    /// go into HttpOnly cookies and never into the body; an API session receives them in the body.
    /// </summary>
    public static class AuthSessionResponses
    {
        public static AuthenticatedSessionResponse Create(
            HttpResponse response,
            Token token,
            string? returnPath = null)
        {
            string? refreshToken = null;
            string? sessionBindingToken = null;

            if (token.Transport.UsesBrowserCookies())
            {
                HttpUtility.SetBrowserRefreshSession(
                    response,
                    token.RefreshToken,
                    token.SessionBindingToken,
                    token.RefreshTokenLifetime
                );
            }
            else
            {
                refreshToken = token.RefreshToken;
                sessionBindingToken = token.SessionBindingToken;
            }

            return new AuthenticatedSessionResponse(
                token.AccessToken,
                token.AccessTokenExpiresAtUtc,
                refreshToken,
                sessionBindingToken,
                returnPath
            );
        }
    }
}
