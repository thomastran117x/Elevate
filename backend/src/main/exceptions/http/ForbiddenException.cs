namespace backend.main.exceptions.http
{
    public class ForbiddenException : AppException
    {
        private const string DefaultMessage = "Forbidden";
        private const int code = StatusCodes.Status403Forbidden;
        private const string DefaultErrorCode = "FORBIDDEN";

        public ForbiddenException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public ForbiddenException(string message)
            : base(message, code, DefaultErrorCode) { }

        public ForbiddenException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
