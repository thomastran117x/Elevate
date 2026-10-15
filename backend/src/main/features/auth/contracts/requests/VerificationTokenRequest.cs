namespace backend.main.features.auth.contracts.requests
{
    public sealed class VerificationTokenRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        public required string Token
        {
            get; set;
        }
        [RequestText(RequestTextKind.Structured)]
        public string? Transport
        {
            get; set;
        }
    }
}
