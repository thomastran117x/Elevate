namespace backend.main.Exceptions
{
    public class UnprocessableEntityException : AppException
    {
        private const string DefaultMessage = "Unprocessable entity";
        private const int code = StatusCodes.Status422UnprocessableEntity;

        public UnprocessableEntityException()
            : base(DefaultMessage, code) { }

        public UnprocessableEntityException(string message)
            : base(message, code) { }

        public UnprocessableEntityException(string message, string details)
            : base(message, code, details) { }
    }
}
