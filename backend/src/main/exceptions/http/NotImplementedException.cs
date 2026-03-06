namespace backend.main.exceptions.http
{
    public class NotImplementedException : AppException
    {
        private const string DefaultMessage = "The service is not implemented yet";
        private const int code = StatusCodes.Status501NotImplemented;

        public NotImplementedException()
            : base(DefaultMessage, code) { }

        public NotImplementedException(string message)
            : base(message, code) { }

        public NotImplementedException(string message, string details)
            : base(message, code, details) { }
    }
}
