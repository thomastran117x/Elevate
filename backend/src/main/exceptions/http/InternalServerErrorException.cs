namespace backend.main.exceptions.http
{
    public class InternalServerErrorException : AppException
    {
        private const string DefaultMessage = "Internal server error";
        private const int code = StatusCodes.Status500InternalServerError;
        private const string DefaultErrorCode = "INTERNAL_SERVER_ERROR";

        public InternalServerErrorException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public InternalServerErrorException(string message)
            : base(message, code, DefaultErrorCode) { }

        public InternalServerErrorException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
