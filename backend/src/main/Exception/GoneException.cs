namespace backend.main.Exceptions
{
    public class GoneException : AppException
    {

        private const string DefaultMessage = "Resource is gone.";
        private const int code = StatusCodes.Status410Gone;

        public GoneException()
            : base(DefaultMessage, code) { }

        public GoneException(string message)
            : base(message, code) { }

        public GoneException(string message, string details)
            : base(message, code, details) { }
    }
}
