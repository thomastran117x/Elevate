namespace backend.main.exceptions.http
{
    public abstract class AppException : Exception
    {
        public string ErrorCode
        {
            get;
        }
        public int StatusCode
        {
            get;
        }
        public object? Details
        {
            get;
        }

        protected AppException(string message, int statusCode, string errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
        }

        protected AppException(string message, int statusCode, string errorCode, object? details)
            : base(message)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
            Details = details;
        }
    }
}
