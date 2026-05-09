namespace backend.main.exceptions.http
{
    public class MethodNotAllowedException : AppException
    {
        private const string DefaultMessage = "Method is not allowed";
        private const int code = StatusCodes.Status405MethodNotAllowed;
        private const string DefaultErrorCode = "METHOD_NOT_ALLOWED";

        public MethodNotAllowedException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public MethodNotAllowedException(string message)
            : base(message, code, DefaultErrorCode) { }

        public MethodNotAllowedException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
