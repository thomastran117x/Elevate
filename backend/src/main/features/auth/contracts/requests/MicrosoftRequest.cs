namespace backend.main.features.auth.contracts.requests
{
    public class MicrosoftRequest : OAuthRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        public string? Nonce
        {
            get; set;
        }
    }
}
