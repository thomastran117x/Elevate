namespace backend.main.infrastructure.elasticsearch
{
    public abstract class ElasticsearchServiceException : Exception
    {
        protected ElasticsearchServiceException(string message)
            : base(message) { }

        protected ElasticsearchServiceException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public sealed class ElasticsearchDisabledException : ElasticsearchServiceException
    {
        public ElasticsearchDisabledException(string message)
            : base(message) { }
    }

    public sealed class ElasticsearchConfigurationException : ElasticsearchServiceException
    {
        public ElasticsearchConfigurationException(string message)
            : base(message) { }

        public ElasticsearchConfigurationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public sealed class ElasticsearchUnavailableException : ElasticsearchServiceException
    {
        public ElasticsearchUnavailableException(string message)
            : base(message) { }

        public ElasticsearchUnavailableException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
