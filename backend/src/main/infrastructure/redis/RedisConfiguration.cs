using backend.main.application.environment;
using backend.main.features.cache;
using backend.main.utilities.implementation;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Polly;
using Polly.Retry;

using StackExchange.Redis;

namespace backend.main.infrastructure.redis
{
    public static class RedisConfig
    {
        private static readonly AsyncRetryPolicy _retryPolicy =
            Policy
                .Handle<RedisConnectionException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)),
                    onRetry: (ex, delay, attempt, _) =>
                    {
                        Logger.Warn(
                            $"Redis connection attempt {attempt} failed. Retrying in {delay.TotalMilliseconds} ms."
                        );
                    });

        public static IServiceCollection AddAppRedis(
            this IServiceCollection services,
            IConfiguration _)
        {
            var health = new RedisHealth();
            var noOp = new NoOpCacheService();
            var state = new RedisReconnectState(noOp);

            try
            {
                _retryPolicy.ExecuteAsync(async () =>
                {
                    var mux = await ConnectionMultiplexer.ConnectAsync(
                        EnvironmentSetting.RedisConnection);

                    var db = mux.GetDatabase();

                    await db.PingAsync();

                    var resource = new RedisResource(mux);
                    state.SwitchToRedis(new CacheService(resource));

                    services.AddSingleton<IConnectionMultiplexer>(mux);
                    services.AddSingleton(resource);

                    health.IsAvailable = true;

                    Logger.Info("Redis connection established successfully.");
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                health.IsAvailable = false;
                health.Failure = ex;

                Logger.Warn(
                    ex,
                    "Redis unavailable after retries. Using in-memory fallback; will retry connection in background."
                );

                services.AddHostedService<RedisReconnectBackgroundService>();
            }

            services.AddSingleton(health);
            services.TryAddSingleton(state);
            services.AddSingleton<ICacheService>(sp =>
                new CacheServiceProxy(sp.GetRequiredService<RedisReconnectState>()));
            return services;
        }
    }
}
