namespace backend.main.features.auth.passkeys
{
    /// <summary>
    /// One registered WebAuthn credential (a passkey). Only public material is stored: the
    /// authenticator keeps the private key, and nothing here can sign.
    /// </summary>
    /// <remarks>
    /// The credential id is globally unique, so it can never be registered to a second account.
    /// <see cref="IsBackupEligible"/> is fixed at registration and decides the counter policy; an
    /// assertion that reports a different value is refused.
    /// </remarks>
    public sealed class PasskeyCredential
    {
        public const int CredentialIdMaxBytes = 1023;
        public const int FriendlyNameMaxLength = 64;
        public const int TransportsMaxLength = 128;
        public const int AttestationFormatMaxLength = 32;

        public int Id
        {
            get; set;
        }

        public int UserId
        {
            get; set;
        }

        /// <summary>The authenticator's credential id (raw bytes, not base64url).</summary>
        public required byte[] CredentialId
        {
            get; set;
        }

        /// <summary>The COSE-encoded public key from the attestation.</summary>
        public required byte[] PublicKey
        {
            get; set;
        }

        /// <summary>The account's user handle when the credential was registered.</summary>
        public required byte[] UserHandle
        {
            get; set;
        }

        /// <summary>The highest signature counter seen. Zero for authenticators that do not count.</summary>
        public long SignatureCounter
        {
            get; set;
        }

        /// <summary>The authenticator model, or all zeros when it was not disclosed.</summary>
        public Guid AaGuid
        {
            get; set;
        }

        /// <summary>Comma-separated WebAuthn transport hints, e.g. <c>internal,hybrid</c>.</summary>
        public string? Transports
        {
            get; set;
        }

        public string? AttestationFormat
        {
            get; set;
        }

        /// <summary>The BE flag at registration: the credential can sync between devices.</summary>
        public bool IsBackupEligible
        {
            get; set;
        }

        /// <summary>The BS flag last reported: the credential is currently backed up or synced.</summary>
        public bool IsBackedUp
        {
            get; set;
        }

        public required string FriendlyName
        {
            get; set;
        }

        public DateTime CreatedAtUtc
        {
            get; set;
        }

        public DateTime? LastUsedAtUtc
        {
            get; set;
        }

        /// <summary>When an assertion last reported a counter that did not advance.</summary>
        public DateTime? CounterAnomalyAtUtc
        {
            get; set;
        }
    }
}
