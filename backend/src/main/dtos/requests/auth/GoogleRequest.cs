namespace backend.main.dtos.requests.auth
{
    public class GoogleRequest : OAuthRequest
    {
        public string? Nonce { get; set; }
    }
}
