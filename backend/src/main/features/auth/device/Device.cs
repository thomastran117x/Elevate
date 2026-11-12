namespace backend.main.features.auth.device;

public class Device
{
    /// <summary>
    /// How long a completed sign-in verification trusts the device. Measured from that
    /// verification: signing in on the trusted device does not extend it, verifying again does.
    /// </summary>
    public static readonly TimeSpan TrustLifetime = TimeSpan.FromDays(90);

    public int Id
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public required string DeviceTokenHash
    {
        get; set;
    }
    public required string DeviceType
    {
        get; set;
    }
    public required string ClientName
    {
        get; set;
    }
    public string IpAddress { get; set; } = "Unknown";
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the trust ends. Enforced by the server rather than left to the cookie's own expiry,
    /// which the client controls.
    /// </summary>
    public DateTime ExpiresAt
    {
        get; set;
    }
}


