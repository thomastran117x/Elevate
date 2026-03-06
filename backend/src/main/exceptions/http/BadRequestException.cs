namespace backend.main.exceptions.http
{
    public class BadRequestException : AppException
    {
        private const string DefaultMessage = "Bad request";
        private const int code = StatusCodes.Status400BadRequest;

        public BadRequestException()
            : base(DefaultMessage, code) { }

        public BadRequestException(string message)
            : base(message, code) { }

        public BadRequestException(string message, string details)
            : base(message, code, details) { }
    }
}
