namespace backend.main.exceptions.http
{
    public class ResourceNotFoundException : AppException
    {
        private const string DefaultMessage = "The requested resouce is not found";
        private const int code = StatusCodes.Status404NotFound;
        private const string DefaultErrorCode = "RESOURCE_NOT_FOUND";

        public ResourceNotFoundException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public ResourceNotFoundException(string message)
            : base(message, code, DefaultErrorCode) { }

        public ResourceNotFoundException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
