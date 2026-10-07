namespace backend.main.shared.exceptions.http
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

        /// <summary>A conflict a client needs to tell apart from others, by its own code.</summary>
        protected ConflictException(string message, string errorCode, object? details)
            : base(message, code, errorCode, details) { }
    }
}
