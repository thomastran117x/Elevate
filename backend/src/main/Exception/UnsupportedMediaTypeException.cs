namespace backend.main.Exceptions
{
    public class UnsupportedMediaTypeException : AppException
    {
        private const string DefaultMessage = "Media type is not supported";
        private const int code = StatusCodes.Status415UnsupportedMediaType;

        public UnsupportedMediaTypeException()
            : base(DefaultMessage, code) { }

        public UnsupportedMediaTypeException(string message)
            : base(message, code) { }

        public UnsupportedMediaTypeException(string message, string details)
            : base(message, code, details) { }
    }
}
