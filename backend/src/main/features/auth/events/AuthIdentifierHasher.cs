using System.Security.Cryptography;
using System.Text;

using backend.main.features.auth.abuse;
using backend.main.features.profile;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.events;

public enum AuthSecurityTargetKind
{
    Username,
    Email,
}

/// <summary>
/// The account identifier an event is aimed at when no account id is known, such as a failed
/// sign-in for a name that may not exist. Only its keyed hash ever leaves the emitter.
/// </summary>
public readonly record struct AuthSecurityTarget(AuthSecurityTargetKind Kind, string? Value)
{
    public static AuthSecurityTarget Username(string? value) => new(AuthSecurityTargetKind.Username, value);

    public static AuthSecurityTarget Email(string? value) => new(AuthSecurityTargetKind.Email, value);
}

/// <summary>
/// Pseudonymizes contact and network identifiers with HMAC-SHA256, so events about the same
/// account or source can be correlated without the value being recoverable by hashing a
/// dictionary of addresses, which an unkeyed hash of an IPv4 address or a common email allows.
/// </summary>
/// <remarks>
/// The key is read once at startup on purpose: changing it mid-run would split hashes of the same
/// identifier across the change, so a new key takes effect on restart.
/// </remarks>
public sealed class AuthIdentifierHasher
{
    /// <summary>Hex characters kept from the digest: 128 bits.</summary>
    internal const int HashLength = 32;

    private readonly byte[] _key;

    public AuthIdentifierHasher(IOptions<AuthSecurityEventsOptions> options)
    {
        var configured = options.Value.IdentifierHashKey;
        _key = Convert.FromBase64String(
            string.IsNullOrWhiteSpace(configured)
                ? AuthSecurityEventsOptions.DevelopmentHashKey
                : configured);
    }

    public string? HashTarget(AuthSecurityTarget target)
    {
        var normalized = target.Kind switch
        {
            AuthSecurityTargetKind.Username => UsernamePolicy.Normalize(target.Value ?? string.Empty),
            AuthSecurityTargetKind.Email => EmailPolicy.Normalize(target.Value),
            _ => string.Empty,
        };

        return normalized.Length == 0
            ? null
            : Hash(AuthSecurityWireNames.Of(target.Kind), normalized);
    }

    public string? HashSourceIp(string? sourceIp)
    {
        var normalized = AuthAbuseProtectionService.NormalizeIp(sourceIp ?? string.Empty);
        return normalized == "unknown" ? null : Hash("source_ip", normalized);
    }

    private string Hash(string kind, string normalized)
    {
        var digest = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{kind}:{normalized}"));
        return Convert.ToHexStringLower(digest)[..HashLength];
    }
}
