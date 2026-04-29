namespace backend.main.dtos.requests.auth
{
    public class RefreshTokenRequest
    {
        public string? RefreshToken { get; set; }
        public string? SessionBindingToken { get; set; }
    }
}
