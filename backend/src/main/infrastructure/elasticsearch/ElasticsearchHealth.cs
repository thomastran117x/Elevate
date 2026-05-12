namespace backend.main.infrastructure.elasticsearch
{
    public sealed class ElasticsearchHealth
    {
        public bool IsConfigured
        {
            get; internal set;
        }
        public bool IsAvailable
        {
            get; internal set;
        }
        public Exception? Failure
        {
            get; internal set;
        }
    }
}
