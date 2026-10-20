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
