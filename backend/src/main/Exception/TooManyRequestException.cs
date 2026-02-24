namespace backend.main.Exceptions
{
    public class TooManyRequestException : AppException
    {
        private const string DefaultMessage = "Too many requests.";
        private const int code = StatusCodes.Status429TooManyRequests;

        public TooManyRequestException()
            : base(DefaultMessage, code) { }

        public TooManyRequestException(string message)
            : base(message, code) { }

        public TooManyRequestException(string message, string details)
            : base(message, code, details) { }
    }
}
