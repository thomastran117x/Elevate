namespace backend.main.Exceptions
{
    public class NotFoundException : AppException
    {
        private const string DefaultMessage = "The requested resouce is not found";
        private const int code = StatusCodes.Status404NotFound;

        public NotFoundException()
            : base(DefaultMessage, code) { }

        public NotFoundException(string message)
            : base(message, code) { }

        public NotFoundException(string message, string details)
            : base(message, code, details) { }
    }
}
