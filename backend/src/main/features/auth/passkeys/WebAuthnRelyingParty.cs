using System.Text.Json;

using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.passkeys
{
    /// <summary>
    /// <see cref="IWebAuthnRelyingParty"/> on the Fido2 library (ADR 0002). The library parses and
    /// verifies; this class only maps our configuration and results onto it.
    /// </summary>
    public sealed class WebAuthnRelyingParty : IWebAuthnRelyingParty
    {
        private readonly IFido2 _fido2;

        public WebAuthnRelyingParty(IOptions<PasskeyOptions> options)
            : this(CreateLibrary(options.Value))
        {
        }

        internal WebAuthnRelyingParty(IFido2 fido2)
        {
            _fido2 = fido2;
        }

        public string CreateRegistrationOptions(
            byte[] userHandle,
            string userName,
            string displayName,
            IReadOnlyList<byte[]> excludeCredentialIds)
        {
            var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
            {
                User = new Fido2User
                {
                    Id = userHandle,
                    Name = userName,
                    DisplayName = displayName,
                },
                ExcludeCredentials = excludeCredentialIds
                    .Select(id => new PublicKeyCredentialDescriptor(id))
                    .ToList(),
                // A discoverable credential is what lets passkey sign-in find the account without
                // a username, and user verification is what makes the passkey a second factor.
                AuthenticatorSelection = new AuthenticatorSelection
                {
                    ResidentKey = ResidentKeyRequirement.Required,
                    UserVerification = UserVerificationRequirement.Required,
                },
                AttestationPreference = AttestationConveyancePreference.None,
                Extensions = new AuthenticationExtensionsClientInputs { CredProps = true },
            });

            return options.ToJson();
        }

        public async Task<VerifiedPasskeyRegistration> VerifyRegistrationAsync(
            string optionsJson,
            JsonElement response,
            Func<byte[], CancellationToken, Task<bool>> isCredentialIdUnused,
            CancellationToken cancellationToken = default)
        {
            var raw = Deserialize<AuthenticatorAttestationRawResponse>(response);
            var options = CredentialCreateOptions.FromJson(optionsJson);

            try
            {
                var credential = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
                {
                    AttestationResponse = raw,
                    OriginalOptions = options,
                    IsCredentialIdUniqueToUserCallback = (args, ct) => isCredentialIdUnused(args.CredentialId, ct),
                }, cancellationToken);

                return new VerifiedPasskeyRegistration(
                    credential.Id,
                    credential.PublicKey,
                    credential.SignCount,
                    credential.AaGuid,
                    credential.Transports is { Length: > 0 } transports
                        ? string.Join(',', transports.Select(t => t.ToEnumMemberValue()))
                        : null,
                    credential.AttestationFormat,
                    credential.IsBackupEligible,
                    credential.IsBackedUp);
            }
            catch (Fido2VerificationException ex)
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Rejected, ex.Code.ToString(), ex);
            }
            catch (Exception ex) when (IsParseFailure(ex))
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, ex.GetType().Name, ex);
            }
        }

        public string CreateAssertionOptions(IReadOnlyList<byte[]> allowedCredentialIds)
        {
            var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = allowedCredentialIds
                    .Select(id => new PublicKeyCredentialDescriptor(id))
                    .ToList(),
                UserVerification = UserVerificationRequirement.Required,
            });

            return options.ToJson();
        }

        public PasskeyAssertionCandidate ReadAssertion(JsonElement response)
        {
            var raw = Deserialize<AuthenticatorAssertionRawResponse>(response);
            try
            {
                var parsed = AuthenticatorAssertionResponse.Parse(raw);
                if (raw.RawId is not { Length: > 0 })
                    throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, "The credential id is missing.");

                return new PasskeyAssertionCandidate(raw.RawId, parsed.UserHandle, parsed.AuthenticatorData.IsBackupEligible);
            }
            catch (Fido2VerificationException ex)
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, ex.Code.ToString(), ex);
            }
        }

        public async Task<VerifiedPasskeyAssertion> VerifyAssertionAsync(
            string optionsJson,
            JsonElement response,
            PasskeyCredential stored,
            bool enforceCounter,
            CancellationToken cancellationToken = default)
        {
            var raw = Deserialize<AuthenticatorAssertionRawResponse>(response);
            var options = AssertionOptions.FromJson(optionsJson);

            try
            {
                var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
                {
                    AssertionResponse = raw,
                    OriginalOptions = options,
                    StoredPublicKey = stored.PublicKey,
                    // A stored counter of 0 is the library's "this authenticator does not count",
                    // so it skips the check only for credentials whose counter we judge ourselves.
                    StoredSignatureCounter = enforceCounter ? ToCounter(stored.SignatureCounter) : 0,
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                        args.CredentialId.AsSpan().SequenceEqual(stored.CredentialId)
                        && args.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
                }, cancellationToken);

                return new VerifiedPasskeyAssertion(result.SignCount, result.IsBackedUp);
            }
            catch (Fido2VerificationException ex) when (ex.Code == Fido2ErrorCode.InvalidSignCount)
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.CounterRegressed, ex.Code.ToString(), ex);
            }
            catch (Fido2VerificationException ex)
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Rejected, ex.Code.ToString(), ex);
            }
            catch (Exception ex) when (IsParseFailure(ex))
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, ex.GetType().Name, ex);
            }
        }

        private static IFido2 CreateLibrary(PasskeyOptions options) =>
            new Fido2(new Fido2Configuration
            {
                RPID = options.RelyingPartyId,
                RPName = options.RelyingPartyName,
                Origins = new HashSet<string>(options.Origins, StringComparer.OrdinalIgnoreCase),
                Timeout = (uint)options.CeremonyTimeout.TotalMilliseconds,
                // Ceremonies run in the top-level frontend page, never inside another site's frame.
                AllowCrossOriginRequests = false,
            });

        private static T Deserialize<T>(JsonElement response) where T : class
        {
            if (response.ValueKind != JsonValueKind.Object)
                throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, "The response is not a JSON object.");

            try
            {
                return response.Deserialize<T>()
                    ?? throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, "The response is empty.");
            }
            catch (Exception ex) when (IsParseFailure(ex))
            {
                throw new WebAuthnVerificationException(WebAuthnFailure.Malformed, ex.GetType().Name, ex);
            }
        }

        private static bool IsParseFailure(Exception ex) =>
            ex is JsonException or FormatException or ArgumentException or InvalidOperationException
                or NotSupportedException or OverflowException;

        private static uint ToCounter(long stored) =>
            stored <= 0 ? 0 : stored >= uint.MaxValue ? uint.MaxValue : (uint)stored;
    }
}
