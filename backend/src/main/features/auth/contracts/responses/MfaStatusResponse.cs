namespace backend.main.features.auth.contracts.responses
{
    public sealed class MfaStatusResponse
    {
        public bool EnrollmentAvailable
        {
            get; init;
        }

        public bool IsSmsMfaEnabled
        {
            get; init;
        }

        public string? MaskedPhoneNumber
        {
            get; init;
        }

        public DateTime? PhoneVerifiedAtUtc
        {
            get; init;
        }
    }
}
