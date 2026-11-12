using backend.main.features.auth.oauth.links;
using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.passkeys
{
    /// <summary>A passkey change refused for a reason the client shows by its code.</summary>
    public sealed class PasskeyConflictException : ConflictException
    {
        /// <summary>The account already holds as many passkeys as it may.</summary>
        public const string LimitReachedCode = "PASSKEY_LIMIT_REACHED";

        private PasskeyConflictException(string message, string code)
            : base(message, code, null)
        {
        }

        public static PasskeyConflictException LimitReached() => new(
            "You've added as many passkeys as an account can have. Remove one before adding another.",
            LimitReachedCode);

        /// <summary>
        /// Shares the provider-unlink code, so a client handles both removals the same way.
        /// </summary>
        public static PasskeyConflictException LastSignInMethod() => new(
            "This passkey is your only way to sign in. Set a password, link a provider, or add another passkey before removing it.",
            ProviderLinkConflictException.LastSignInMethodCode);
    }
}
