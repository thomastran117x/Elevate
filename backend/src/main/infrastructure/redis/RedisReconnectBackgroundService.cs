using backend.main.application.environment;
using backend.main.features.cache;
using backend.main.shared.utilities.logger;

using StackExchange.Redis;

namespace backend.main.infrastructure.redis
{
    /// <summary>
    /// Periodically attempts to connect to Redis while the app is using the in-memory fallback.
    /// On success, switches the cache to Redis and stops retrying.
    /// </summary>
    public sealed class RedisReconnectBackgroundService : BackgroundService
    {
        private const int RetryIntervalSeconds = 15;

        private readonly IServiceProvider _services;
        private readonly RedisHealth _health;

        public RedisReconnectBackgroundService(IServiceProvider services, RedisHealth health)
        {
            _services = services;
            _health = health;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_health.IsAvailable)
                {
                    await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), stoppingToken)
                        .ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var mux = await ConnectionMultiplexer.ConnectAsync(
                        EnvironmentSetting.RedisConnection).ConfigureAwait(false);

                    var db = mux.GetDatabase();
                    await db.PingAsync().ConfigureAwait(false);

                    var resource = new RedisResource(mux);
                    var redisCache = new CacheService(resource);

                    var state = _services.GetRequiredService<RedisReconnectState>();
                    state.SwitchToRedis(redisCache);

                    _health.IsAvailable = true;
                    _health.Failure = null;

                    Logger.Info("Redis reconnection succeeded. Cache switched from in-memory to Redis.");
                    return;
                }
                catch (Exception ex)
                {
                    _health.Failure = ex;
                    Logger.Warn(
                        ex,
                        $"Redis reconnect attempt failed. Will retry in {RetryIntervalSeconds}s.");
                }

                await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
