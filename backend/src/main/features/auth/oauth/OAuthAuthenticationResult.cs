using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;

namespace backend.main.features.auth.oauth
{
    public sealed class OAuthAuthenticationResult
    {
        public required string Type
        {
            get; init;
        }
        public AuthenticatedSessionResult? Session
        {
            get; init;
        }
        public PendingOAuthSignupChallenge? PendingSignup
        {
            get; init;
        }
        public LoginStepUpChallengeResponse? StepUp
        {
            get; init;
        }

        public PendingProviderLinkChallenge? LinkConfirmation
        {
            get; init;
        }

        public bool RequiresRoleSelection => Type == AuthFlowResponseTypes.RequiresRoleSelection;
        public UserToken? UserToken => Session?.UserToken;

        public static OAuthAuthenticationResult Authenticated(AuthenticatedSessionResult session) =>
            new()
            {
                Type = AuthFlowResponseTypes.Authenticated,
                Session = session
            };

        public static OAuthAuthenticationResult RequiresStepUp(LoginStepUpChallengeResponse stepUp) =>
            new()
            {
                Type = AuthFlowResponseTypes.RequiresStepUp,
                StepUp = stepUp
            };

        public static OAuthAuthenticationResult RoleSelectionRequired(
            PendingOAuthSignupChallenge pendingSignup
        ) =>
            new()
            {
                Type = AuthFlowResponseTypes.RequiresRoleSelection,
                PendingSignup = pendingSignup
            };

        public static OAuthAuthenticationResult LinkConfirmationRequired(
            PendingProviderLinkChallenge linkConfirmation
        ) =>
            new()
            {
                Type = AuthFlowResponseTypes.RequiresLinkConfirmation,
                LinkConfirmation = linkConfirmation
            };
    }

    /// <summary>
    /// A provider sign-in whose verified email matched an account with no provider link. Its
    /// owner signs in to the account and confirms with <see cref="LinkToken"/>.
    /// </summary>
    public sealed class PendingProviderLinkChallenge
    {
        public required string LinkToken
        {
            get; init;
        }

        public required string Provider
        {
            get; init;
        }

        public required string MaskedEmail
        {
            get; init;
        }

        public DateTime ExpiresAtUtc
        {
            get; init;
        }
    }

    public sealed class PendingOAuthSignupChallenge
    {
        public required string SignupToken
        {
            get; init;
        }
        public required string Email
        {
            get; init;
        }
        public required string Name
        {
            get; init;
        }
        public required string Provider
        {
            get; init;
        }
    }
}
