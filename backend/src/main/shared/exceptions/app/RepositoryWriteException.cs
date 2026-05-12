using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class RepositoryWriteException : NotAvailableException
    {
        private const string DefaultMessage = "Unable to write to the database.";

        public RepositoryWriteException()
            : base(DefaultMessage) { }

        public RepositoryWriteException(string message)
            : base(message) { }

        public RepositoryWriteException(string message, string details)
            : base(message, details) { }
    }
}
