namespace backend.main.DTOs
{
    public class MessageResponse
    {
        public string Message { get; set; } = string.Empty;
        public string? Details
        {
            get; set;
        }

        public MessageResponse(string message, string? details = null)
        {
            Message = message;
            Details = details;
        }

    }
    public class ApiResponse<T>
    {
        public string Message { get; set; } = string.Empty;
        public T? Data
        {
            get; set;
        }

        public ApiResponse(string message, T data)
        {
            Message = message;
            Data = data;
        }
    }
}
