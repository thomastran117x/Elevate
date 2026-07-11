namespace backend.main.features.auth.contracts.responses
{
    public sealed class SessionMfaOptionsResponse
    {
        public required string[] AvailableMethods
        {
            get; set;
        }

        public string? MaskedPhone
        {
            get; set;
        }

        public required string MaskedEmail
        {
            get; set;
        }
    }
}
