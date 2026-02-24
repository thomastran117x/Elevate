namespace backend.main.Exceptions
{
    public class InternalServerException : AppException
    {
        private const string DefaultMessage = "Internal server error";
        private const int code = StatusCodes.Status500InternalServerError;

        public InternalServerException()
            : base(DefaultMessage, code) { }

        public InternalServerException(string message)
            : base(message, code) { }

        public InternalServerException(string message, string details)
            : base(message, code, details) { }
    }
}
