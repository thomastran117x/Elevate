namespace backend.main.features.auth.mfa.totp
{
    public sealed class TotpMfaEnrollment
    {
        public int UserId
        {
            get; set;
        }

        public required string EncryptedSecret
        {
            get; set;
        }

        public int EncryptionKeyVersion
        {
            get; set;
        } = 1;

        public bool IsTotpMfaEnabled
        {
            get; set;
        }

        public DateTime? EnrolledAtUtc
        {
            get; set;
        }

        public DateTime? DisabledAtUtc
        {
            get; set;
        }

        public DateTime CreatedAtUtc
        {
            get; set;
        } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc
        {
            get; set;
        } = DateTime.UtcNow;
    }
}
