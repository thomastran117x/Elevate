namespace backend.main.exceptions.http
{
    public class UnsupportedMediaTypeException : AppException
    {
        private const string DefaultMessage = "Media type is not supported";
        private const int code = StatusCodes.Status415UnsupportedMediaType;
        private const string DefaultErrorCode = "UNSUPPORTED_MEDIA_TYPE";

        public UnsupportedMediaTypeException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public UnsupportedMediaTypeException(string message)
            : base(message, code, DefaultErrorCode) { }

        public UnsupportedMediaTypeException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
