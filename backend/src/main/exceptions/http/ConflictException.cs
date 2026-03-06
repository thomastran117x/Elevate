namespace backend.main.exceptions.http
{
    public class ConflictException : AppException
    {
        private const string DefaultMessage = "Conflict";
        private const int code = StatusCodes.Status409Conflict;

        public ConflictException()
            : base(DefaultMessage, code) { }

        public ConflictException(string message)
            : base(message, code) { }

        public ConflictException(string message, string details)
            : base(message, code, details) { }
    }
}
