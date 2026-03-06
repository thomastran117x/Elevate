namespace backend.main.utilities.implementation
{
    public static class HttpUtility
    {
        public static void SetRefreshTokenCookie(HttpResponse response, string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7)
            };

            response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
        }
    }
}
