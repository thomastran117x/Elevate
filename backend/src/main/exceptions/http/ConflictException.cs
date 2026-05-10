namespace backend.main.exceptions.http
{
    public class ConflictException : AppException
    {
        private const string DefaultMessage = "Conflict";
        private const int code = StatusCodes.Status409Conflict;
        private const string DefaultErrorCode = "CONFLICT";

        public ConflictException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public ConflictException(string message)
            : base(message, code, DefaultErrorCode) { }

        public ConflictException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
