namespace backend.main.features.auth.history;

/// <summary>
/// API-side switches for the account security history. Retention and alerts are configured on the
/// security-history worker, which owns the table.
/// </summary>
public sealed class SecurityHistoryOptions
{
    public const string SectionName = "Auth:SecurityHistory";

    /// <summary>Hand eligible events to the security-history topic. Read live; no restart needed.</summary>
    public bool PublishEnabled { get; set; } = true;

    /// <summary>Record the masked source network (IPv4 /24, IPv6 /48) with each entry.</summary>
    public bool IncludeMaskedNetwork { get; set; } = true;
}
