namespace backend.main.features.auth.contracts.requests
{
    public class RefreshTokenRequest
    {
        public string? RefreshToken { get; set; }
        public string? SessionBindingToken { get; set; }
    }
}
