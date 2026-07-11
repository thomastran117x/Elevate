using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    public sealed class SessionMfaOptionsResponse
    {
        [JsonPropertyName("availableMethods")]
        public required string[] AvailableMethods
        {
            get; set;
        }

        [JsonPropertyName("maskedPhone")]
        public string? MaskedPhone
        {
            get; set;
        }

        [JsonPropertyName("maskedEmail")]
        public required string MaskedEmail
        {
            get; set;
        }
    }
}
