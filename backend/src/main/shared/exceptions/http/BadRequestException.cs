namespace backend.main.shared.exceptions.http
{
    public class BadRequestException : AppException
    {
        private const string DefaultMessage = "Bad request";
        private const int code = StatusCodes.Status400BadRequest;
        private const string DefaultErrorCode = "BAD_REQUEST";

        public BadRequestException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public BadRequestException(string message)
            : base(message, code, DefaultErrorCode) { }

        public BadRequestException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
