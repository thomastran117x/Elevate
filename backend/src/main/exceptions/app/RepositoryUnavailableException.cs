using backend.main.exceptions.http;

namespace backend.main.exceptions.app
{
    public class RepositoryUnavailableException : NotAvailableException
    {
        private const string DefaultMessage = "The database is temporarily unavailable.";

        public RepositoryUnavailableException()
            : base(DefaultMessage) { }

        public RepositoryUnavailableException(string message)
            : base(message) { }

        public RepositoryUnavailableException(string message, string details)
            : base(message, details) { }
    }
}
