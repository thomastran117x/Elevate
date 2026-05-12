namespace backend.main.features.auth.contracts.requests
{
    public class GoogleRequest : OAuthRequest
    {
        public string? Nonce { get; set; }
    }
}
