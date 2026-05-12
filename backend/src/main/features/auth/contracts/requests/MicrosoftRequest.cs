namespace backend.main.features.auth.contracts.requests
{
    public class MicrosoftRequest : OAuthRequest
    {
        public string? Nonce { get; set; }
    }
}
