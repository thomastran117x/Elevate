using System.Text.Json.Serialization;

namespace backend.main.shared.responses
{
    /// <summary>
    /// Structured error payload returned inside <see cref="ApiResponse{T}"/>.
    /// </summary>
    public class ApiError
    {
        public ApiError(string code, object? details = null)
        {
            Code = code;
            Details = details;
        }

        /// <summary>
        /// Machine-readable error code.
        /// </summary>
        [JsonPropertyName("code")]
        public string Code
        {
            get; set;
        }

        /// <summary>
        /// Additional endpoint-specific error details.
        /// </summary>
        [JsonPropertyName("details")]
        public object? Details
        {
            get; set;
        }
    }
}
