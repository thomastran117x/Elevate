using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using backend.main.features.auth.passkeys;

namespace backend.main.features.auth.contracts.requests
{
    /// <summary>Starts registering a passkey on the signed-in account.</summary>
    public sealed class PasskeyRegistrationOptionsRequest
    {
        /// <summary>A name to tell this passkey apart, e.g. "Work laptop". Defaults to "Passkey".</summary>
        [RequestText(RequestTextKind.HumanSingleLine)]
        [MaxLength(PasskeyCredential.FriendlyNameMaxLength)]
        public string? Name
        {
            get; set;
        }
    }

    /// <summary>Completes a passkey ceremony with what the authenticator returned.</summary>
    public class PasskeyCeremonyCompletionRequest
    {
        /// <summary>The ceremony id the options response returned.</summary>
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [MaxLength(128)]
        public required string CeremonyId
        {
            get; set;
        }

        /// <summary>
        /// The <c>PublicKeyCredential</c> from <c>navigator.credentials.create()</c> or
        /// <c>get()</c>, serialized as WebAuthn JSON (its <c>toJSON()</c> form).
        /// </summary>
        [Required]
        public required JsonElement Credential
        {
            get; set;
        }
    }

    /// <summary>Completes passkey sign-in.</summary>
    public sealed class PasskeySignInRequest : PasskeyCeremonyCompletionRequest
    {
        public bool RememberMe { get; set; } = false;

        /// <summary><c>browser</c> (cookies, the default) or <c>api</c> (tokens in the body).</summary>
        [RequestText(RequestTextKind.Structured)]
        public string? Transport
        {
            get; set;
        }
    }

    public sealed class RenamePasskeyRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        [MinLength(1)]
        [MaxLength(PasskeyCredential.FriendlyNameMaxLength)]
        public required string Name
        {
            get; set;
        }
    }
}
