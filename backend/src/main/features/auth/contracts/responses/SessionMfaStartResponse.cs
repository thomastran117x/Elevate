namespace backend.main.features.auth.contracts.responses
{
    public sealed class SessionMfaStartResponse
    {
        public required string SelectedMethod
        {
            get; set;
        }

        public required string MaskedDestination
        {
            get; set;
        }

        public DateTime ExpiresAtUtc
        {
            get; set;
        }

        public DateTime CooldownEndsAtUtc
        {
            get; set;
        }
    }
}
