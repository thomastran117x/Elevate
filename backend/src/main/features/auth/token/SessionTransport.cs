using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.token
{
    public enum SessionTransport
    {
        BrowserCookie,
        ApiToken,
    }

    public static class SessionTransportResolver
    {
        public const string BrowserValue = "browser";
        public const string ApiValue = "api";

        public static SessionTransport ResolveOrDefault(string? transport)
        {
            if (string.IsNullOrWhiteSpace(transport))
                return SessionTransport.BrowserCookie;

            return transport.Trim().ToLowerInvariant() switch
            {
                BrowserValue => SessionTransport.BrowserCookie,
                ApiValue => SessionTransport.ApiToken,
                _ => throw new BadRequestException(
                    $"Transport must be one of: {BrowserValue}, {ApiValue}."
                ),
            };
        }

        public static bool UsesBrowserCookies(this SessionTransport transport) =>
            transport == SessionTransport.BrowserCookie;
    }
}
