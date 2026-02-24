using StackExchange.Redis;

namespace backend.main.Resources
{
    public sealed class RedisHealth
    {
        public bool IsAvailable
        {
            get; internal set;
        }
        public Exception? Failure
        {
            get; internal set;
        }
    }

    public class RedisResource
    {
        public IDatabase Database
        {
            get;
        }
        public IConnectionMultiplexer Multiplexer
        {
            get;
        }

        public RedisResource(IConnectionMultiplexer multiplexer)
        {
            Multiplexer = multiplexer;
            Database = multiplexer.GetDatabase();
        }
    }
}
