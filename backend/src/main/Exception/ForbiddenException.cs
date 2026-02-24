namespace backend.main.Exceptions
{
    public class ForbiddenException : AppException
    {
        private const string DefaultMessage = "Forbidden";
        private const int code = StatusCodes.Status403Forbidden;

        public ForbiddenException()
            : base(DefaultMessage, code) { }

        public ForbiddenException(string message)
            : base(message, code) { }

        public ForbiddenException(string message, string details)
            : base(message, code, details) { }
    }
}
