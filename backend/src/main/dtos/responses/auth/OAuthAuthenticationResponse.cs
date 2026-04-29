namespace backend.main.dtos.responses.auth
{
    public sealed class OAuthAuthenticationResponse
    {
        public bool RequiresRoleSelection { get; set; }
        public AuthResponse? Auth { get; set; }
        public string? SignupToken { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Provider { get; set; }
    }
}
