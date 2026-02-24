using backend.main.Resources;
using backend.main.Utilities;

using Polly;
using Polly.Retry;

using StackExchange.Redis;

namespace backend.main.Config
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

            try
            {
                _retryPolicy.ExecuteAsync(async () =>
                {
                    var mux = await ConnectionMultiplexer.ConnectAsync(
                        EnvManager.RedisConnection);

                    var db = mux.GetDatabase();

                    await db.PingAsync();

                    services.AddSingleton<IConnectionMultiplexer>(mux);
                    services.AddSingleton(new RedisResource(mux));

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
                    "Redis unavailable after retries."
                );
            }

            services.AddSingleton(health);
            return services;
        }
    }
}
