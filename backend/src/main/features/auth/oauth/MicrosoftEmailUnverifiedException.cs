using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.oauth
{
    /// <summary>
    /// A Microsoft sign-in matched an existing account by email, but Microsoft did not prove the
    /// caller controls that address, so the account is neither linked nor signed in.
    /// </summary>
    public sealed class MicrosoftEmailUnverifiedException : ConflictException
    {
        public const string Code = "OAUTH_EMAIL_UNVERIFIED";

        public const string DefaultMessage =
            "An account already uses this email address, and Microsoft did not confirm that you own it. " +
            "Sign in to that account another way, or use a different Microsoft account.";

        public MicrosoftEmailUnverifiedException()
            : base(DefaultMessage, Code, null)
        {
        }
    }
}
