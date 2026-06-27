namespace backend.main.features.auth.contracts.responses
{
    public sealed class TotpEnrollmentStartResponse
    {
        public required string SecretKey
        {
            get; init;
        }

        public required string QrCodeUri
        {
            get; init;
        }

        public DateTime ExpiresAtUtc
        {
            get; init;
        }
    }
}
