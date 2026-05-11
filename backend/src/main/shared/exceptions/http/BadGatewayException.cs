namespace backend.main.shared.exceptions.http
{
    public class BadGatewayException : AppException
    {
        private const string DefaultMessage = "Bad gateway";
        private const int code = StatusCodes.Status502BadGateway;
        private const string DefaultErrorCode = "BAD_GATEWAY";

        public BadGatewayException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public BadGatewayException(string message)
            : base(message, code, DefaultErrorCode) { }

        public BadGatewayException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
