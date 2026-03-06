namespace backend.main.exceptions.http
{
    public class UnauthorizedException : AppException
    {
        private const string DefaultMessage = "Unauthorized";
        private const int code = StatusCodes.Status401Unauthorized;

        public UnauthorizedException()
            : base(DefaultMessage, code) { }

        public UnauthorizedException(string message)
            : base(message, code) { }

        public UnauthorizedException(string message, string details)
            : base(message, code, details) { }
    }
}
