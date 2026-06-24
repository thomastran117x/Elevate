using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    public sealed class OAuthAuthenticationResponse
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
        public OAuthRoleSelectionResponse? RoleSelection
        {
            get; set;
        }

        [JsonIgnore]
        public bool RequiresRoleSelection => Type == AuthFlowResponseTypes.RequiresRoleSelection;

        [JsonIgnore]
        public string? SignupToken => RoleSelection?.SignupToken;
    }
}
