using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    public sealed class SessionMfaStartResponse
    {
        [JsonPropertyName("selectedMethod")]
        public required string SelectedMethod
        {
            get; set;
        }

        [JsonPropertyName("maskedDestination")]
        public required string MaskedDestination
        {
            get; set;
        }

        [JsonPropertyName("expiresAtUtc")]
        public DateTime ExpiresAtUtc
        {
            get; set;
        }

        [JsonPropertyName("cooldownEndsAtUtc")]
        public DateTime CooldownEndsAtUtc
        {
            get; set;
        }
    }
}
