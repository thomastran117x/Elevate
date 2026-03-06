namespace backend.main.dtos.responses.general
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
}
