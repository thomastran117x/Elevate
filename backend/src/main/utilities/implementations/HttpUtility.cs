namespace backend.main.utilities.implementation
{
    public static class HttpUtility
    {
        private const string RefreshCookieName = "refreshToken";
        private const string RefreshCookiePath = "/";

        public static void SetRefreshTokenCookie(HttpResponse response, string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7),
                Path = RefreshCookiePath
            };

            response.Cookies.Append(RefreshCookieName, refreshToken, cookieOptions);
        }

        public static void ClearRefreshTokenCookie(HttpResponse response)
        {
            response.Cookies.Delete(RefreshCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath
            });
        }
    }
}
