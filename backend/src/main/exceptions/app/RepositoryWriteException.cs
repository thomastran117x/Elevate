using backend.main.exceptions.http;

namespace backend.main.exceptions.app
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
