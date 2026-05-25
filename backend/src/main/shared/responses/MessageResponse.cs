namespace backend.main.shared.responses
{
    /// <summary>
    /// Convenience response envelope for endpoints that only return a message.
    /// </summary>
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
