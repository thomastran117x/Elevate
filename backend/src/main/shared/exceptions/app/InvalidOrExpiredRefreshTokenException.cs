using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class InvalidOrExpiredRefreshTokenException : UnauthorizedException
    {
        private const string DefaultMessage = "Invalid or expired refresh token.";

        public InvalidOrExpiredRefreshTokenException()
            : base(DefaultMessage) { }

        public InvalidOrExpiredRefreshTokenException(string message)
            : base(message) { }

        public InvalidOrExpiredRefreshTokenException(string message, string details)
            : base(message, details) { }
    }
}
