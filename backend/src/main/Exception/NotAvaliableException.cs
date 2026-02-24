namespace backend.main.Exceptions
{
    public class NotAvaliableException : AppException
    {
        private const string DefaultMessage = "The service is not avaliable";
        private const int code = StatusCodes.Status503ServiceUnavailable;

        public NotAvaliableException()
            : base(DefaultMessage, code) { }

        public NotAvaliableException(string message)
            : base(message, code) { }

        public NotAvaliableException(string message, string details)
            : base(message, code, details) { }
    }
}
