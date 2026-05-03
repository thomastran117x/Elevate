namespace backend.main.dtos.responses.general
{
    public class ApiResponse<T>
    {
        public string Message { get; set; } = string.Empty;
        public T? Data
        {
            get; set;
        }
        public string? Source { get; set; }

        public ApiResponse(string message, T data, string? source = null)
        {
            Message = message;
            Data = data;
            Source = source;
        }
    }
}
