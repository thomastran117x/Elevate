using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class EmailAlreadyExistsException : ConflictException
    {
        private const string DefaultMessage = "An account with this email already exists.";

        public EmailAlreadyExistsException()
            : base(DefaultMessage) { }

        public EmailAlreadyExistsException(string message)
            : base(message) { }

        public EmailAlreadyExistsException(string message, string details)
            : base(message, details) { }
    }
}
