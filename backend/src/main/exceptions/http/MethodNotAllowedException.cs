namespace backend.main.exceptions.http
{
    public class MethodNotAllowedException : AppException
    {
        private const string DefaultMessage = "Method is not allowed";
        private const int code = StatusCodes.Status405MethodNotAllowed;

        public MethodNotAllowedException()
            : base(DefaultMessage, code) { }

        public MethodNotAllowedException(string message)
            : base(message, code) { }

        public MethodNotAllowedException(string message, string details)
            : base(message, code, details) { }
    }
}
