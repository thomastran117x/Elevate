using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    /// <summary>
    /// A newly generated set of recovery codes. This response is the only place the plaintext
    /// ever appears; it cannot be fetched again.
    /// </summary>
    public sealed class RecoveryCodesResponse
    {
        [JsonPropertyName("codes")]
        public required IReadOnlyList<string> Codes
        {
            get; set;
        }

        [JsonPropertyName("generatedAtUtc")]
        public DateTime GeneratedAtUtc
        {
            get; set;
        }
    }
}
