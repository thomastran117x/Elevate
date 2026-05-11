using backend.main.infrastructure.redis;
using backend.main.shared.utilities.logger;

using StackExchange.Redis;

namespace backend.main.features.cache
{
    public abstract class BaseCacheService
    {
        protected readonly IDatabase _db;
        protected readonly IConnectionMultiplexer _redis;

        protected BaseCacheService(
            RedisResource redisResource
        )
        {
            _db = redisResource.Database;
            _redis = redisResource.Multiplexer;
        }

        protected async Task<T> ExecuteAsync<T>(
            Func<Task<T>> action,
            T fallback = default!
        )
        {
            try
            {
                return await action();
            }
            catch (RedisTimeoutException ex)
            {
                Logger.Warn(ex, "Redis timeout");
                return fallback;
            }
            catch (RedisConnectionException ex)
            {
                Logger.Warn(ex, "Redis connection error");
                return fallback;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unexpected Redis error");
                return fallback;
            }
        }
    }
}
