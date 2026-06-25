namespace backend.main.features.auth.contracts.responses
{
    public sealed class StartLoginStepUpResponse
    {
        public required string Challenge
        {
            get; set;
        }
        public DateTime ExpiresAtUtc
        {
            get; set;
        }
        public required string SelectedMethod
        {
            get; set;
        }
        public required string MaskedDestination
        {
            get; set;
        }
        public DateTime CooldownEndsAtUtc
        {
            get; set;
        }
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
