namespace backend.main.shared.exceptions.http
{
    public class GatewayTimeoutException : AppException
    {
        private const string DefaultMessage = "Gateway timeout";
        private const int code = StatusCodes.Status504GatewayTimeout;
        private const string DefaultErrorCode = "GATEWAY_TIMEOUT";

        public GatewayTimeoutException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public GatewayTimeoutException(string message)
            : base(message, code, DefaultErrorCode) { }

        public GatewayTimeoutException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
