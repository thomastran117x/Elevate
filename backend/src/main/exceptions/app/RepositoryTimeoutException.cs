using backend.main.exceptions.http;

namespace backend.main.exceptions.app
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
