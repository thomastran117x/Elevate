namespace backend.main.exceptions.http
{
    public class ResourceNotFoundException : AppException
    {
        private const string DefaultMessage = "The requested resouce is not found";
        private const int code = StatusCodes.Status404NotFound;

        public ResourceNotFoundException()
            : base(DefaultMessage, code) { }

        public ResourceNotFoundException(string message)
            : base(message, code) { }

        public ResourceNotFoundException(string message, string details)
            : base(message, code, details) { }
    }
}
