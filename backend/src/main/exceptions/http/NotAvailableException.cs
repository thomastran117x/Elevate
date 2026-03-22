namespace backend.main.exceptions.http
{
    public class NotAvailableException : AppException
    {
        private const string DefaultMessage = "The service is not available";
        private const int code = StatusCodes.Status503ServiceUnavailable;

        public NotAvailableException()
            : base(DefaultMessage, code) { }

        public NotAvailableException(string message)
            : base(message, code) { }

        public NotAvailableException(string message, string details)
            : base(message, code, details) { }
    }
}
