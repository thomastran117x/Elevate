using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class RepositoryTimeoutException : GatewayTimeoutException
    {
        private const string DefaultMessage = "The database operation timed out.";

        public RepositoryTimeoutException()
            : base(DefaultMessage) { }

        public RepositoryTimeoutException(string message)
            : base(message) { }

        public RepositoryTimeoutException(string message, string details)
            : base(message, details) { }
    }
}
