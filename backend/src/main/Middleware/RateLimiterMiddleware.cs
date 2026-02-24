using backend.main.Config;
using backend.main.Interfaces;
using backend.main.Resources;

namespace backend.main.Middlewares
{
    public sealed class NoopRateLimiter
    {
        public Task<(bool allowed, TimeSpan? retryAfter)> AllowAllAsync()
            => Task.FromResult<(bool, TimeSpan?)>((true, null));
    }

    public class RedisRateLimiter
    {
        private readonly ICacheService _cache;

        public RedisRateLimiter(ICacheService cache)
        {
            _cache = cache;
        }

        public async Task<(bool allowed, TimeSpan? retryAfter)> FixedWindowAsync(
            string key,
            int limit,
            TimeSpan window)
        {
            var count = await _cache.IncrementAsync(key);

            if (count == 1)
                await _cache.SetExpiryAsync(key, window);

            if (count > limit)
            {
                var ttl = await _cache.GetTTLAsync(key);
                return (false, ttl);
            }

            return (true, null);
        }
        public async Task<(bool allowed, TimeSpan? retryAfter)> TokenBucketAsync(
            string key,
            int tokenLimit,
            int refillAmount,
            TimeSpan refillPeriod)
        {
            var tokensKey = $"{key}:tokens";
            var lastRefillKey = $"{key}:ts";

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var lastTsRaw = await _cache.GetValueAsync(lastRefillKey);
            var lastTs = lastTsRaw != null ? long.Parse(lastTsRaw) : now;

            var delta = now - lastTs;
            var refillTokens = (delta / (long)refillPeriod.TotalSeconds) * refillAmount;

            var currentRaw = await _cache.GetValueAsync(tokensKey);
            var current = currentRaw != null ? long.Parse(currentRaw) : tokenLimit;

            var newCount = Math.Min(tokenLimit, current + refillTokens);

            await _cache.SetValueAsync(tokensKey, newCount.ToString(), refillPeriod);
            await _cache.SetValueAsync(lastRefillKey, now.ToString(), refillPeriod);

            if (newCount <= 0)
                return (false, refillPeriod);

            await _cache.DecrementAsync(tokensKey, 1);
            return (true, null);
        }
    }

    public static class RateLimiterMiddleware
    {
        public static IServiceCollection AddAppRateLimiter(
            this IServiceCollection services,
            RateLimitOptions options)
        {
            services.AddScoped<RedisRateLimiter>();
            services.AddSingleton<NoopRateLimiter>();
            services.AddSingleton(options);

            return services;
        }
    }

    public class RedisRateLimitMiddleware
    {
        private readonly RequestDelegate _next;

        public RedisRateLimitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            RedisHealth redisHealth,
            RedisRateLimiter redisLimiter,
            RateLimitOptions options)
        {
            if (!redisHealth.IsAvailable)
            {
                await _next(context);
                return;
            }

            var key =
                context.User.Identity?.IsAuthenticated == true
                    ? $"rl:user:{context.User.FindFirst("sub")?.Value}"
                    : $"rl:ip:{context.Connection.RemoteIpAddress}";

            var result =
                options.Strategy == RateLimitStrategy.FixedWindow
                    ? await redisLimiter.FixedWindowAsync(
                        key,
                        options.PermitLimit,
                        options.Window)
                    : await redisLimiter.TokenBucketAsync(
                        key,
                        options.TokenLimit,
                        options.TokensPerPeriod,
                        options.ReplenishmentPeriod);

            if (!result.allowed)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Slow down.",
                    path = context.Request.Path,
                    retryAfter = result.retryAfter?.TotalSeconds
                });

                return;
            }

            await _next(context);
        }
    }
}
