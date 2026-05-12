namespace backend.main.shared.exceptions.http
{
    public class TooManyRequestException : AppException
    {
        private const string DefaultMessage = "Too many requests.";
        private const int code = StatusCodes.Status429TooManyRequests;
        private const string DefaultErrorCode = "TOO_MANY_REQUESTS";

        public TooManyRequestException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public TooManyRequestException(string message)
            : base(message, code, DefaultErrorCode) { }

        public TooManyRequestException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
