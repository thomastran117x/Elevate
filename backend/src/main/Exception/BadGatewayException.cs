namespace backend.main.Exceptions
{
    public class BadGatewayException : AppException
    {
        private const string DefaultMessage = "Bad gateway";
        private const int code = StatusCodes.Status502BadGateway;

        public BadGatewayException()
            : base(DefaultMessage, code) { }

        public BadGatewayException(string message)
            : base(message, code) { }

        public BadGatewayException(string message, string details)
            : base(message, code, details) { }
    }
}
