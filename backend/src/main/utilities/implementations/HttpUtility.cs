using backend.main.dtos.general;
using backend.main.configurations.application;

namespace backend.main.utilities.implementation
{
    public static class HttpUtility
    {
        public const string RefreshCookieName = "refreshToken";
        public const string RefreshTokenHeaderName = "X-Refresh-Token";
        private const string RefreshCookiePath = RoutePaths.ApiAuthPath;
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

        public static bool ShouldUseRefreshCookie(ClientRequestInfo requestInfo)
        {
            return requestInfo.IsBrowserClient;
        }

        public static string? ResolveRefreshToken(HttpRequest request, string? refreshToken = null)
        {
            if (request.Cookies.TryGetValue(RefreshCookieName, out var cookieRefreshToken)
                && !string.IsNullOrWhiteSpace(cookieRefreshToken))
                return cookieRefreshToken;

            var headerRefreshToken = request.Headers[RefreshTokenHeaderName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerRefreshToken))
                return headerRefreshToken;

            return string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken;
        }

        public static string? ApplyRefreshToken(
            HttpResponse response,
            ClientRequestInfo requestInfo,
            string refreshToken,
            TimeSpan? lifetime = null
        )
        {
            if (!ShouldUseRefreshCookie(requestInfo))
                return refreshToken;

            response.Cookies.Append(RefreshCookieName, refreshToken, BuildRefreshCookieOptions(lifetime));
            return null;
        }

        public static void ClearRefreshToken(HttpResponse response, ClientRequestInfo requestInfo)
        {
            if (!ShouldUseRefreshCookie(requestInfo))
                return;

            response.Cookies.Delete(RefreshCookieName, BuildRefreshCookieOptions());
        }

        public static void SetRefreshTokenCookie(
            HttpResponse response,
            string refreshToken,
            TimeSpan? lifetime = null
        )
        {
            response.Cookies.Append(RefreshCookieName, refreshToken, BuildRefreshCookieOptions(lifetime));
        }

        public static void ClearRefreshTokenCookie(HttpResponse response)
        {
            response.Cookies.Delete(RefreshCookieName, BuildRefreshCookieOptions());
        }

        private static CookieOptions BuildRefreshCookieOptions(TimeSpan? lifetime = null)
        {
            var refreshLifetime = lifetime ?? RefreshTokenLifetime;

            return new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                MaxAge = refreshLifetime,
                Expires = DateTime.UtcNow.Add(refreshLifetime),
                Path = RefreshCookiePath
            };
        }
    }
}
