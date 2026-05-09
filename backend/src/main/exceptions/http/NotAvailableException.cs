namespace backend.main.exceptions.http
{
    public class NotAvailableException : AppException
    {
        private const string DefaultMessage = "The service is not available";
        private const int code = StatusCodes.Status503ServiceUnavailable;
        private const string DefaultErrorCode = "NOT_AVAILABLE";

        public NotAvailableException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public NotAvailableException(string message)
            : base(message, code, DefaultErrorCode) { }

        public NotAvailableException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
