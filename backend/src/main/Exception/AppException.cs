namespace backend.main.Exceptions
{
    public abstract class AppException : Exception
    {
        public int StatusCode
        {
            get;
        }
        public string? Details
        {
            get;
        }

        protected AppException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }

        protected AppException(string message, int statusCode, string? details)
            : base(message)
        {
            StatusCode = statusCode;
            Details = details;
        }
    }
}
