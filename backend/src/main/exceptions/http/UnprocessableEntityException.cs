namespace backend.main.exceptions.http
{
    public class UnprocessableEntityException : AppException
    {
        private const string DefaultMessage = "Unprocessable entity";
        private const int code = StatusCodes.Status422UnprocessableEntity;
        private const string DefaultErrorCode = "UNPROCESSABLE_ENTITY";

        public UnprocessableEntityException()
            : base(DefaultMessage, code, DefaultErrorCode) { }

        public UnprocessableEntityException(string message)
            : base(message, code, DefaultErrorCode) { }

        public UnprocessableEntityException(string message, string details)
            : base(message, code, DefaultErrorCode, details) { }
    }
}
