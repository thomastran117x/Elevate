using backend.main.application.environment;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.events;

public sealed class AuthSecurityEventsOptions
{
    public const string SectionName = "Auth:SecurityEvents";

    /// <summary>The smallest HMAC key accepted, in bytes.</summary>
    public const int MinimumHashKeyBytes = 32;

    /// <summary>
    /// The key development and test hosts fall back to. It is public, so hashes made with it are
    /// pseudonymous only in name; startup refuses it anywhere else.
    /// </summary>
    internal const string DevelopmentHashKey = "ZGV2ZWxvcG1lbnRfb25seV9hdXRoX2V2ZW50X2hhc2hfa2V5";

    public bool LogSinkEnabled { get; set; } = true;

    public bool MetricsSinkEnabled { get; set; } = true;

    /// <summary>
    /// Base64 HMAC-SHA256 key for hashing usernames, emails, and source addresses in events.
    /// Required outside development and test.
    /// </summary>
    public string? IdentifierHashKey
    {
        get; set;
    }

    public bool IncludeSourceIpHash { get; set; } = true;

    public bool IncludeTargetHash { get; set; } = true;
}

public sealed class AuthSecurityEventsOptionsValidator : IValidateOptions<AuthSecurityEventsOptions>
{
    private readonly bool _isNonProduction;

    public AuthSecurityEventsOptionsValidator(IConfiguration config)
        : this(BypassEnvironment.IsNonProduction(BypassEnvironment.ResolveName(config)))
    {
    }

    internal AuthSecurityEventsOptionsValidator(bool isNonProduction)
    {
        _isNonProduction = isNonProduction;
    }

    public ValidateOptionsResult Validate(string? name, AuthSecurityEventsOptions options)
    {
        var key = options.IdentifierHashKey;
        if (string.IsNullOrWhiteSpace(key) || key == AuthSecurityEventsOptions.DevelopmentHashKey)
        {
            // The environment resolver defaults to production, so a host that forgot to name its
            // environment fails closed here rather than hashing with a published key.
            return _isNonProduction
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "Auth:SecurityEvents:IdentifierHashKey must be configured outside development and test."
                );
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("Auth:SecurityEvents:IdentifierHashKey is not valid base64.");
        }

        return bytes.Length >= AuthSecurityEventsOptions.MinimumHashKeyBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Auth:SecurityEvents:IdentifierHashKey must decode to at least {AuthSecurityEventsOptions.MinimumHashKeyBytes} bytes."
            );
    }
}
