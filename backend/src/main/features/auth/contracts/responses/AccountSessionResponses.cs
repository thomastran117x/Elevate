namespace backend.main.features.auth.contracts.responses
{
    /// <summary>The caller's signed-in sessions.</summary>
    public sealed class AuthSessionsResponse
    {
        public required List<AuthSessionResponse> Sessions
        {
            get; set;
        }
    }

    /// <summary>
    /// One live session. Classification fields are snake_case names from closed sets; token
    /// material and the raw source address are never included.
    /// </summary>
    public sealed class AuthSessionResponse
    {
        /// <summary>The session id, which revocation takes.</summary>
        public required string Id
        {
            get; set;
        }

        /// <summary>The session making this request.</summary>
        public bool Current
        {
            get; set;
        }

        /// <summary><c>browser</c> or <c>api</c>.</summary>
        public required string Transport
        {
            get; set;
        }

        /// <summary>The client family, e.g. <c>chrome</c>, or <c>unknown</c>.</summary>
        public required string Client
        {
            get; set;
        }

        /// <summary>The device class, e.g. <c>desktop</c>, or <c>unknown</c>.</summary>
        public required string DeviceClass
        {
            get; set;
        }

        /// <summary>The last source network prefix, e.g. <c>203.0.113.0/24</c>.</summary>
        public string? Network
        {
            get; set;
        }

        public bool RememberMe
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

        /// <summary>When the session ends unless it is used again.</summary>
        public DateTime ExpiresAt
        {
            get; set;
        }
    }

    /// <summary>The outcome of a session revocation.</summary>
    public sealed class SessionRevocationResponse
    {
        public int RevokedCount
        {
            get; set;
        }

        /// <summary>
        /// The session making the request was among those revoked: its tokens no longer work, and
        /// its browser cookies were cleared.
        /// </summary>
        public bool CurrentSessionRevoked
        {
            get; set;
        }
    }

    /// <summary>The caller's trusted devices.</summary>
    public sealed class TrustedDevicesResponse
    {
        public required List<TrustedDeviceResponse> Devices
        {
            get; set;
        }
    }

    /// <summary>
    /// One device that skips sign-in verification until <see cref="ExpiresAt"/>. The device token,
    /// its hash, and the raw source address are never included.
    /// </summary>
    public sealed class TrustedDeviceResponse
    {
        public int Id
        {
            get; set;
        }

        /// <summary>The device making this request presented this device's token.</summary>
        public bool Current
        {
            get; set;
        }

        /// <summary>The client family, e.g. <c>chrome</c>, or <c>unknown</c>.</summary>
        public required string Client
        {
            get; set;
        }

        /// <summary>The device class, e.g. <c>desktop</c>, or <c>unknown</c>.</summary>
        public required string DeviceClass
        {
            get; set;
        }

        /// <summary>The last source network prefix, e.g. <c>203.0.113.0/24</c>.</summary>
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

        /// <summary>When the trust ends and sign-in asks for verification again.</summary>
        public DateTime ExpiresAt
        {
            get; set;
        }
    }

    /// <summary>The outcome of forgetting trusted devices.</summary>
    public sealed class TrustedDeviceRevocationResponse
    {
        public int RevokedCount
        {
            get; set;
        }

        /// <summary>
        /// The device making the request was among those forgotten, and its cookie was cleared.
        /// </summary>
        public bool CurrentDeviceRevoked
        {
            get; set;
        }
    }
}
