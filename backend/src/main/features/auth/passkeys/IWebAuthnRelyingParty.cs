using System.Text.Json;

namespace backend.main.features.auth.passkeys
{
    /// <summary>
    /// The WebAuthn relying party: builds ceremony options and verifies what an authenticator
    /// returned. It is the only code that knows the server library (see ADR 0002), so options and
    /// responses cross it as WebAuthn JSON and its results are plain records.
    /// </summary>
    /// <remarks>
    /// Every option it builds requires user verification, and registration also requires a
    /// discoverable credential and asks for no attestation. A response that is malformed, names the
    /// wrong ceremony, challenge, origin, or RP ID, or carries a bad signature throws
    /// <see cref="WebAuthnVerificationException"/>; nothing else about the failure leaves it.
    /// </remarks>
    public interface IWebAuthnRelyingParty
    {
        /// <summary>Options for <c>navigator.credentials.create()</c>, serialized as WebAuthn JSON.</summary>
        string CreateRegistrationOptions(
            byte[] userHandle,
            string userName,
            string displayName,
            IReadOnlyList<byte[]> excludeCredentialIds);

        Task<VerifiedPasskeyRegistration> VerifyRegistrationAsync(
            string optionsJson,
            JsonElement response,
            Func<byte[], CancellationToken, Task<bool>> isCredentialIdUnused,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Options for <c>navigator.credentials.get()</c>. An empty list asks for any discoverable
        /// passkey for this RP ID, which is how passkey sign-in finds the account.
        /// </summary>
        string CreateAssertionOptions(IReadOnlyList<byte[]> allowedCredentialIds);

        /// <summary>
        /// Reads which credential signed an assertion and the user handle it reported, before
        /// anything is verified, so the caller can look up the stored passkey.
        /// </summary>
        PasskeyAssertionCandidate ReadAssertion(JsonElement response);

        /// <param name="enforceCounter">
        /// False skips the library's counter check, for backup-eligible credentials whose counter
        /// is not meaningful across synced copies. The caller then judges the counter itself.
        /// </param>
        Task<VerifiedPasskeyAssertion> VerifyAssertionAsync(
            string optionsJson,
            JsonElement response,
            PasskeyCredential stored,
            bool enforceCounter,
            CancellationToken cancellationToken = default);
    }

    /// <summary>A verified registration: the public half of a new passkey.</summary>
    public sealed record VerifiedPasskeyRegistration(
        byte[] CredentialId,
        byte[] PublicKey,
        uint SignatureCounter,
        Guid AaGuid,
        string? Transports,
        string? AttestationFormat,
        bool IsBackupEligible,
        bool IsBackedUp);

    /// <param name="IsBackupEligible">The BE flag the assertion reported.</param>
    public sealed record PasskeyAssertionCandidate(byte[] CredentialId, byte[]? UserHandle, bool IsBackupEligible);

    public sealed record VerifiedPasskeyAssertion(uint SignatureCounter, bool IsBackedUp);

    /// <summary>
    /// A response that failed verification. <see cref="Reason"/> is for logs and security events
    /// only; clients always get the same generic refusal.
    /// </summary>
    public sealed class WebAuthnVerificationException : Exception
    {
        public WebAuthnVerificationException(WebAuthnFailure reason, string detail, Exception? inner = null)
            : base(detail, inner)
        {
            Reason = reason;
        }

        public WebAuthnFailure Reason
        {
            get;
        }
    }

    public enum WebAuthnFailure
    {
        /// <summary>The response could not be parsed.</summary>
        Malformed,

        /// <summary>The response parsed, but the library refused it.</summary>
        Rejected,

        /// <summary>The signature counter did not advance on a device-bound credential.</summary>
        CounterRegressed,
    }
}
