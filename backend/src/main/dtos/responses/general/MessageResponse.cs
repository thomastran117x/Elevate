namespace backend.main.dtos.responses.general
{
    public class MessageResponse : ApiResponse<object?>
    {
        public MessageResponse(string message)
            : base(message, null) { }

        public MessageResponse(string message, object? meta)
            : base(message, null)
        {
            Meta = meta;
        }
    }
}
