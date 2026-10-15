namespace backend.main.features.auth.contracts.requests
{
    public class GoogleRequest : OAuthRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        public string? Nonce
        {
            get; set;
        }
    }
}
