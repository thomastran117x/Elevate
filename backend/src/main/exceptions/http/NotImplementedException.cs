namespace backend.main.exceptions.http
{
    public class NotImplementedException : AppException
    {
        private const string DefaultMessage = "The service is not implemented yet";
        private const int code = StatusCodes.Status501NotImplemented;
        private const string DefaultErrorCode = "NOT_IMPLEMENTED";

        public NotImplementedException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public NotImplementedException(string message)
            : base(message, code, DefaultErrorCode) { }

        public NotImplementedException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
