namespace backend.main.shared.responses
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
