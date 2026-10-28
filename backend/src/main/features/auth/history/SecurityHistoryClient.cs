using backend.main.application.handlers;

namespace backend.main.features.auth.history;

/// <summary>
/// The coarse client family shown in the security history.
/// </summary>
/// <remarks>
/// A closed set on purpose: the User-Agent is attacker-controlled, so only a recognised family is
/// ever stored, never the header or any token taken from it.
/// </remarks>
public enum SecurityHistoryClient
{
    Unknown,
    Chrome,
    Edge,
    Firefox,
    Safari,
    Opera,
    ApiClient,
    Other,
}

public static class SecurityHistoryClients
{
    /// <summary>
    /// Maps a User-Agent onto <see cref="SecurityHistoryClient"/>. The classifier's fallback, the
    /// header's first product token, is exactly the free text the history must never keep, so
    /// anything it does not recognise becomes <see cref="SecurityHistoryClient.Other"/>.
    /// </summary>
    public static SecurityHistoryClient FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return SecurityHistoryClient.Unknown;

        return ClientRequestClassifier.ResolveClientName(userAgent) switch
        {
            "Chrome" => SecurityHistoryClient.Chrome,
            "Edge" => SecurityHistoryClient.Edge,
            "Firefox" => SecurityHistoryClient.Firefox,
            "Safari" => SecurityHistoryClient.Safari,
            "Opera" => SecurityHistoryClient.Opera,
            "Postman" or "cURL" or "Axios" or "HttpClient" => SecurityHistoryClient.ApiClient,
            _ => SecurityHistoryClient.Other,
        };
    }
}
