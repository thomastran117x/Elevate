namespace backend.main.exceptions.http
{
    public class UnauthorizedException : AppException
    {
        private const string DefaultMessage = "Unauthorized";
        private const int code = StatusCodes.Status401Unauthorized;
        private const string DefaultErrorCode = "UNAUTHORIZED";

        public UnauthorizedException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public UnauthorizedException(string message)
            : base(message, code, DefaultErrorCode) { }

        public UnauthorizedException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
