using backend.main.shared.exceptions.http;

namespace backend.main.features.auth.oauth.links
{
    /// <summary>A provider link change refused for a reason the client shows by its code.</summary>
    public sealed class ProviderLinkConflictException : ConflictException
    {
        /// <summary>The provider identity is already linked to a different account.</summary>
        public const string AlreadyLinkedCode = "PROVIDER_ALREADY_LINKED";

        /// <summary>The account already links a different identity for this provider.</summary>
        public const string AlreadyConnectedCode = "PROVIDER_ALREADY_CONNECTED";

        /// <summary>Removing the link would leave the account with no way to sign in.</summary>
        public const string LastSignInMethodCode = "LAST_SIGN_IN_METHOD";

        private ProviderLinkConflictException(string message, string code)
            : base(message, code, null)
        {
        }

        public static ProviderLinkConflictException AlreadyLinked() => new(
            "This account from the provider is already linked to a different account.",
            AlreadyLinkedCode);

        public static ProviderLinkConflictException AlreadyConnected() => new(
            "Your account is already linked to a different account from this provider. Unlink it first.",
            AlreadyConnectedCode);

        public static ProviderLinkConflictException LastSignInMethod() => new(
            "This is your only way to sign in. Set a password or link another provider before removing it.",
            LastSignInMethodCode);
    }

    /// <summary>
    /// The link flow's state or pending link expired, was already used, or never existed. The
    /// client restarts the flow.
    /// </summary>
    public sealed class ProviderLinkExpiredException : AppException
    {
        public const string Code = "PROVIDER_LINK_EXPIRED";

        public ProviderLinkExpiredException()
            : base(
                "This link request has expired or was already used. Start again.",
                StatusCodes.Status410Gone,
                Code)
        {
        }
    }

    /// <summary>
    /// The link flow belongs to a different account or session than the caller's. The flow it
    /// presented is spent, so it cannot be replayed from the right session either.
    /// </summary>
    public sealed class ProviderLinkMismatchException : AppException
    {
        public const string Code = "PROVIDER_LINK_MISMATCH";

        public ProviderLinkMismatchException()
            : base(
                "This link request was started from a different account or session.",
                StatusCodes.Status403Forbidden,
                Code)
        {
        }
    }
}
