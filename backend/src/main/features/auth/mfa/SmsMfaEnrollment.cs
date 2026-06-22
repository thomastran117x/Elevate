namespace backend.main.features.auth.mfa
{
    public sealed class SmsMfaEnrollment
    {
        public int UserId
        {
            get; set;
        }

        public required string PhoneNumber
        {
            get; set;
        }

        public bool IsSmsMfaEnabled
        {
            get; set;
        }

        public DateTime? PhoneVerifiedAtUtc
        {
            get; set;
        }

        public DateTime CreatedAt
        {
            get; set;
        } = DateTime.UtcNow;

        public DateTime UpdatedAt
        {
            get; set;
        } = DateTime.UtcNow;
    }
}
