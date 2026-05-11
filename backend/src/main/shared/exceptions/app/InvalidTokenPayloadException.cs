using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class InvalidTokenPayloadException : UnauthorizedException
    {
        private const string DefaultMessage = "Invalid token payload.";

        public InvalidTokenPayloadException()
            : base(DefaultMessage) { }

        public InvalidTokenPayloadException(string message)
            : base(message) { }

        public InvalidTokenPayloadException(string message, string details)
            : base(message, details) { }
    }
}
