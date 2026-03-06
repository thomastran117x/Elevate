namespace backend.main.exceptions.http
{
    public class InternalServerErrorException : AppException
    {
        private const string DefaultMessage = "Internal server error";
        private const int code = StatusCodes.Status500InternalServerError;

        public InternalServerErrorException()
            : base(DefaultMessage, code) { }

        public InternalServerErrorException(string message)
            : base(message, code) { }

        public InternalServerErrorException(string message, string details)
            : base(message, code, details) { }
    }
}
