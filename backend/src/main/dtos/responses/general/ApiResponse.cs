using System.Text.Json.Serialization;

namespace backend.main.dtos.responses.general
{
    public class ApiResponse<T>
    {
        public ApiResponse(string message, T? data)
            : this(true, message, data, null, null) { }

        public ApiResponse(string message, T? data, string? source)
            : this(true, message, data, null, CreateSourceMeta(source)) { }

        private ApiResponse(bool success, string message, T? data, ApiError? error, object? meta)
        {
            Success = success;
            Message = message;
            Data = data;
            Error = error;
            Meta = meta;
        }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("error")]
        public ApiError? Error { get; set; }

        [JsonPropertyName("meta")]
        public object? Meta { get; set; }

        public static ApiResponse<T> WithMeta(string message, T? data, object? meta) =>
            new(true, message, data, null, meta);

        public static ApiResponse<T> Failure(
            string message,
            string code,
            object? details = null,
            object? meta = null
        ) => new(false, message, default, new ApiError(code, details), meta);

        private static object? CreateSourceMeta(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            return new Dictionary<string, object?>
            {
                ["source"] = source
            };
        }
    }
}
