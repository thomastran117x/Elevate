namespace backend.main.models.other
{
    public sealed class OAuthAuthenticationResult
    {
        public UserToken? UserToken { get; init; }
        public PendingOAuthSignupChallenge? PendingSignup { get; init; }

        public bool RequiresRoleSelection => PendingSignup != null;

        public static OAuthAuthenticationResult Authenticated(UserToken userToken) =>
            new()
            {
                UserToken = userToken
            };

        public static OAuthAuthenticationResult RoleSelectionRequired(
            PendingOAuthSignupChallenge pendingSignup
        ) =>
            new()
            {
                PendingSignup = pendingSignup
            };
    }

    public sealed class PendingOAuthSignupChallenge
    {
        public required string SignupToken { get; init; }
        public required string Email { get; init; }
        public required string Name { get; init; }
        public required string Provider { get; init; }
    }
}
