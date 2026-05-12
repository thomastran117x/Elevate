namespace backend.main.features.auth.contracts.responses
{
    public sealed class OAuthAuthenticationResponse
    {
        public bool RequiresRoleSelection { get; set; }
        public AuthenticatedSessionResponse? Auth { get; set; }
        public string? SignupToken { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Provider { get; set; }
    }
}
