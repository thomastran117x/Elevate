namespace backend.main.features.auth.contracts.responses
{
    public sealed class MfaStatusResponse
    {
        public bool SmsEnrollmentAvailable
        {
            get; init;
        }

        public bool EnrollmentAvailable => SmsEnrollmentAvailable;

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

        public bool TotpEnrollmentAvailable
        {
            get; init;
        }

        public bool IsTotpMfaEnabled
        {
            get; init;
        }

        public DateTime? TotpEnrolledAtUtc
        {
            get; init;
        }
    }
}
