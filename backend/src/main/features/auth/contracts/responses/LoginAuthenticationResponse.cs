namespace backend.main.features.auth.contracts.responses
{
    public sealed class LoginAuthenticationResponse
    {
        public required string Type
        {
            get; set;
        }
        public AuthenticatedSessionResponse? Auth
        {
            get; set;
        }
        public LoginStepUpChallengeResponse? StepUp
        {
            get; set;
        }
    }
}
