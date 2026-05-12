using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class UserNotActiveException : ForbiddenException
    {
        private const string DefaultMessage = "Account is not active.";

        public UserNotActiveException()
            : base(DefaultMessage) { }

        public UserNotActiveException(string message)
            : base(message) { }

        public UserNotActiveException(string message, string details)
            : base(message, details) { }
    }
}
