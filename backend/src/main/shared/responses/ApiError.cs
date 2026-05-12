using System.Text.Json.Serialization;

namespace backend.main.shared.responses
{
    public class ApiError
    {
        public ApiError(string code, object? details = null)
        {
            Code = code;
            Details = details;
        }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("details")]
        public object? Details { get; set; }
    }
}
