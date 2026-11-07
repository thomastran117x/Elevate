using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    /// <summary>How many recovery codes remain, and when the set was made. Never the codes.</summary>
    public sealed class RecoveryCodeStatusResponse
    {
        [JsonPropertyName("remainingCount")]
        public int RemainingCount
        {
            get; set;
        }

        [JsonPropertyName("totalCount")]
        public int TotalCount
        {
            get; set;
        }

        /// <summary>Null when the account has never generated codes.</summary>
        [JsonPropertyName("generatedAtUtc")]
        public DateTime? GeneratedAtUtc
        {
            get; set;
        }
    }
}
