using backend.main.application.environment;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.totp;

namespace backend.main.features.auth.mfa
{
    public sealed class MfaSettingsBuilder : IMfaSettingsBuilder
    {
        private readonly IMfaEnrollmentRepository _smsRepository;
        private readonly ITotpMfaEnrollmentRepository _totpRepository;

        public MfaSettingsBuilder(
            IMfaEnrollmentRepository smsRepository,
            ITotpMfaEnrollmentRepository totpRepository
        )
        {
            _smsRepository = smsRepository;
            _totpRepository = totpRepository;
        }

        public async Task<MfaSettingsResponse> BuildAsync(int userId, string email)
        {
            // These repositories share the scoped AppDatabaseContext, so the reads must stay sequential.
            var smsEnrollment = await _smsRepository.GetByUserIdAsync(userId);
            var totpEnrollment = await _totpRepository.GetByUserIdAsync(userId);

            return new MfaSettingsResponse
            {
                Email = new EmailMfaSettingsDto
                {
                    MaskedEmail = PhoneNumberFormatter.MaskEmail(email),
                    IsEnabled = true,
                },
                Sms = BuildSms(smsEnrollment),
                Totp = BuildTotp(totpEnrollment),
            };
        }

        private static SmsMfaSettingsDto BuildSms(SmsMfaEnrollment? enrollment)
        {
            var isConfigured = enrollment != null && !string.IsNullOrWhiteSpace(enrollment.PhoneNumber);
            var isEnabled = isConfigured && enrollment!.IsSmsMfaEnabled;
            var enrollmentAvailable = EnvironmentSetting.AuthSmsMfaEnrollmentEnabled;

            return new SmsMfaSettingsDto
            {
                EnrollmentAvailable = enrollmentAvailable,
                IsConfigured = isConfigured,
                IsEnabled = isEnabled,
                MaskedPhoneNumber = isConfigured ? PhoneNumberFormatter.Mask(enrollment!.PhoneNumber) : null,
                PhoneVerifiedAtUtc = isConfigured ? enrollment!.PhoneVerifiedAtUtc : null,
                CanEnroll = enrollmentAvailable,
                CanEnable = isConfigured && !isEnabled,
                CanDisable = isEnabled,
                CanRemove = isConfigured,
            };
        }

        private static TotpMfaSettingsDto BuildTotp(TotpMfaEnrollment? enrollment)
        {
            var isConfigured = enrollment != null && !string.IsNullOrWhiteSpace(enrollment.EncryptedSecret);
            var isEnabled = isConfigured && enrollment!.IsTotpMfaEnabled;
            var enrollmentAvailable = EnvironmentSetting.AuthTotpMfaEnrollmentEnabled;

            return new TotpMfaSettingsDto
            {
                EnrollmentAvailable = enrollmentAvailable,
                IsConfigured = isConfigured,
                IsEnabled = isEnabled,
                EnrolledAtUtc = isConfigured ? enrollment!.EnrolledAtUtc : null,
                DisabledAtUtc = isConfigured && !isEnabled ? enrollment!.DisabledAtUtc : null,
                CanEnroll = enrollmentAvailable && !isConfigured,
                CanEnable = isConfigured && !isEnabled,
                CanDisable = isEnabled,
                CanRemove = isConfigured,
            };
        }
    }
}
