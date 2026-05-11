namespace backend.main.shared.exceptions.http
{
    public class GoneException : AppException
    {

        private const string DefaultMessage = "Resource is gone.";
        private const int code = StatusCodes.Status410Gone;
        private const string DefaultErrorCode = "GONE";

        public GoneException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public GoneException(string message)
            : base(message, code, DefaultErrorCode) { }

        public GoneException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
