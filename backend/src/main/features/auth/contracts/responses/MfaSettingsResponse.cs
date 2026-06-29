using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    public sealed class MfaSettingsResponse
    {
        [JsonPropertyName("email")]
        public required EmailMfaSettingsDto Email
        {
            get; init;
        }

        [JsonPropertyName("sms")]
        public required SmsMfaSettingsDto Sms
        {
            get; init;
        }

        [JsonPropertyName("totp")]
        public required TotpMfaSettingsDto Totp
        {
            get; init;
        }
    }

    public sealed class EmailMfaSettingsDto
    {
        [JsonPropertyName("maskedEmail")]
        public required string MaskedEmail
        {
            get; init;
        }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled
        {
            get; init;
        } = true;
    }

    public sealed class SmsMfaSettingsDto
    {
        [JsonPropertyName("enrollmentAvailable")]
        public bool EnrollmentAvailable
        {
            get; init;
        }

        [JsonPropertyName("isConfigured")]
        public bool IsConfigured
        {
            get; init;
        }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled
        {
            get; init;
        }

        [JsonPropertyName("maskedPhoneNumber")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MaskedPhoneNumber
        {
            get; init;
        }

        [JsonPropertyName("phoneVerifiedAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? PhoneVerifiedAtUtc
        {
            get; init;
        }

        [JsonPropertyName("canEnroll")]
        public bool CanEnroll
        {
            get; init;
        }

        [JsonPropertyName("canEnable")]
        public bool CanEnable
        {
            get; init;
        }

        [JsonPropertyName("canDisable")]
        public bool CanDisable
        {
            get; init;
        }

        [JsonPropertyName("canRemove")]
        public bool CanRemove
        {
            get; init;
        }
    }

    public sealed class TotpMfaSettingsDto
    {
        [JsonPropertyName("enrollmentAvailable")]
        public bool EnrollmentAvailable
        {
            get; init;
        }

        [JsonPropertyName("isConfigured")]
        public bool IsConfigured
        {
            get; init;
        }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled
        {
            get; init;
        }

        [JsonPropertyName("enrolledAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? EnrolledAtUtc
        {
            get; init;
        }

        [JsonPropertyName("disabledAtUtc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? DisabledAtUtc
        {
            get; init;
        }

        [JsonPropertyName("canEnroll")]
        public bool CanEnroll
        {
            get; init;
        }

        [JsonPropertyName("canEnable")]
        public bool CanEnable
        {
            get; init;
        }

        [JsonPropertyName("canDisable")]
        public bool CanDisable
        {
            get; init;
        }

        [JsonPropertyName("canRemove")]
        public bool CanRemove
        {
            get; init;
        }
    }
}
