using System.Text.Json;

namespace backend.main.features.auth.contracts.responses
{
    /// <summary>
    /// The options for one passkey ceremony. Pass <see cref="PublicKey"/> to
    /// <c>PublicKeyCredential.parseCreationOptionsFromJSON()</c> or
    /// <c>parseRequestOptionsFromJSON()</c>, and send <see cref="CeremonyId"/> back with the result.
    /// </summary>
    public sealed class PasskeyCeremonyOptionsResponse
    {
        /// <summary>Single use: completing the ceremony spends it, whatever the outcome.</summary>
        public required string CeremonyId
        {
            get; set;
        }

        /// <summary>WebAuthn options JSON. Binary values are base64url strings.</summary>
        public required JsonElement PublicKey
        {
            get; set;
        }

        public DateTime ExpiresAtUtc
        {
            get; set;
        }
    }

    /// <summary>The caller's passkeys.</summary>
    public sealed class PasskeysResponse
    {
        public required List<PasskeyResponse> Passkeys
        {
            get; set;
        }

        /// <summary>How many passkeys one account may register.</summary>
        public int Limit
        {
            get; set;
        }
    }

    /// <summary>
    /// One registered passkey. The credential id, public key, and user handle are never included.
    /// </summary>
    public sealed class PasskeyResponse
    {
        public int Id
        {
            get; set;
        }

        public required string Name
        {
            get; set;
        }

        /// <summary>The passkey can sync between devices, e.g. through a password manager.</summary>
        public bool Syncable
        {
            get; set;
        }

        /// <summary>The authenticator last reported the passkey as backed up or synced.</summary>
        public bool BackedUp
        {
            get; set;
        }

        /// <summary>
        /// False when it is the account's last way to sign in, so removing it is refused.
        /// </summary>
        public bool CanRemove
        {
            get; set;
        }

        public DateTime CreatedAt
        {
            get; set;
        }

        public DateTime? LastUsedAt
        {
            get; set;
        }
    }
}
