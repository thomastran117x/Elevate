using System.Net;

using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.features.auth.token;
using backend.main.shared.requests;

namespace backend.main.features.auth.sessions;

/// <summary>
/// The authoritative record of one signed-in session, keyed by the <c>sid</c> its refresh session
/// and access tokens carry.
/// </summary>
/// <remarks>
/// Redis still holds the refresh secret, generation, and binding hash that rotation needs; this
/// row decides whether the session is alive at all. Access-token validation and every refresh
/// check it, so a revocation written here ends both kinds of token even if the Redis cleanup that
/// follows never lands. It carries no token material, and its client description is the same
/// closed classification and masked network the security history stores, never the raw
/// User-Agent or address.
/// </remarks>
public class AuthSession
{
    /// <summary>The session id, a Guid in "N" format.</summary>
    public required string Id
    {
        get; set;
    }

    public int UserId
    {
        get; set;
    }

    public SessionTransport Transport
    {
        get; set;
    }

    public bool RememberMe
    {
        get; set;
    }

    public SecurityHistoryClient Client { get; set; } = SecurityHistoryClient.Unknown;

    public AuthDeviceClass DeviceClass { get; set; } = AuthDeviceClass.Unknown;

    /// <summary>The last source address truncated to its network prefix (IPv4 /24, IPv6 /48).</summary>
    public string? Network
    {
        get; set;
    }

    public DateTime CreatedAt
    {
        get; set;
    }

    public DateTime LastSeenAt
    {
        get; set;
    }

    /// <summary>
    /// When the session ends unless it is used again. Moved forward with every refresh, like the
    /// Redis key's TTL, and always written after that TTL so it is never the earlier of the two.
    /// </summary>
    public DateTime ExpiresAt
    {
        get; set;
    }

    public DateTime? RevokedAt
    {
        get; set;
    }

    public AuthSecurityReason? RevocationReason
    {
        get; set;
    }

    /// <summary>Describes the client the way the security history does.</summary>
    public void DescribeClient(ClientRequestInfo requestInfo)
    {
        Client = SecurityHistoryClients.FromClientName(requestInfo.ClientName);
        DeviceClass = AuthDeviceClasses.FromDeviceType(requestInfo.DeviceType);
        Network = IPAddress.TryParse(requestInfo.IpAddress, out var address)
            ? NetworkAddressMasker.Mask(address)
            : null;
    }
}

/// <summary>What access-token validation needs, read in one round trip.</summary>
public sealed record AccessTokenSessionState(bool IsDisabled, int AuthVersion, bool SessionActive);
