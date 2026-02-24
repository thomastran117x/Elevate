
namespace backend.main.DTOs
{
    public sealed class EmailMessage
    {
        public required EmailMessageType Type
        {
            get; init;
        }
        public required string Email
        {
            get; init;
        }
        public required string Token
        {
            get; init;
        }
    }
}
