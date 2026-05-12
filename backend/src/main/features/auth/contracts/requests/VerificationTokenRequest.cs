namespace backend.main.features.auth.contracts.requests
{
    public sealed class VerificationTokenRequest
    {
        public required string Token { get; set; }
        public string? Transport { get; set; }
    }
}
