using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class InvalidOrExpiredVerificationTokenException : BadRequestException
    {
        private const string DefaultMessage = "Invalid or expired verification token.";

        public InvalidOrExpiredVerificationTokenException()
            : base(DefaultMessage) { }

        public InvalidOrExpiredVerificationTokenException(string message)
            : base(message) { }

        public InvalidOrExpiredVerificationTokenException(string message, string details)
            : base(message, details) { }
    }
}
