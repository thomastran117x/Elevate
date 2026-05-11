using backend.main.features.cache;

namespace backend.main.infrastructure.redis
{
    /// <summary>
    /// Holds the current cache implementation (NoOp or Redis). When Redis reconnects,
    /// the background service switches this to the Redis-backed cache.
    /// </summary>
    public sealed class RedisReconnectState
    {
        private volatile ICacheService _current;

        public RedisReconnectState(ICacheService initial)
        {
            _current = initial;
        }

        public ICacheService Current => _current;

        /// <summary>
        /// Switches to the Redis-backed cache after a successful reconnection.
        /// Thread-safe.
        /// </summary>
        public void SwitchToRedis(ICacheService redisCache)
        {
            Interlocked.Exchange(ref _current, redisCache);
        }
    }
}
