using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth
{
    public sealed class LoginAuthenticationResult
    {
        public required string Type
        {
            get; init;
        }
        public AuthenticatedSessionResult? Session
        {
            get; init;
        }
        public LoginStepUpChallengeResponse? StepUp
        {
            get; init;
        }

        public static LoginAuthenticationResult Authenticated(AuthenticatedSessionResult session) =>
            new()
            {
                Type = AuthFlowResponseTypes.Authenticated,
                Session = session
            };

        public static LoginAuthenticationResult RequiresStepUp(LoginStepUpChallengeResponse stepUp) =>
            new()
            {
                Type = AuthFlowResponseTypes.RequiresStepUp,
                StepUp = stepUp
            };
    }
}
