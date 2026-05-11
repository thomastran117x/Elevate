using System.Net;

using backend.main.features.cache;

using StackExchange.Redis;

namespace backend.main.application.security
{
    public sealed record SlidingWindowOptions(
        int Limit,
        TimeSpan Window,
        TimeSpan KeyTtl,
        string KeyPrefix = "rl:sw:"
    );

    public static class SlidingWindowRateLimitConfig
    {
        private const string SlidingWindowLua = @"
            local key = KEYS[1]
            local nowMs = tonumber(ARGV[1])
            local windowMs = tonumber(ARGV[2])
            local limit = tonumber(ARGV[3])
            local ttlMs = tonumber(ARGV[4])

            local minScore = nowMs - windowMs

            redis.call('ZREMRANGEBYSCORE', key, 0, minScore)

            local count = redis.call('ZCARD', key)
            if count >= limit then
            return { 0, count }
            end

            -- member must be unique; use nowMs + random suffix
            local member = tostring(nowMs) .. ':' .. tostring(math.random(1, 1000000))
            redis.call('ZADD', key, nowMs, member)
            redis.call('PEXPIRE', key, ttlMs)

            count = count + 1
            return { 1, count }
            ";
        public static IServiceCollection AddSlidingWindowRateLimit(
            this IServiceCollection services,
            SlidingWindowOptions options
        )
        {
            services.AddSingleton(options);
            services.AddSingleton<SlidingWindowRateLimitMiddleware>();
            return services;
        }

        public static IApplicationBuilder UseSlidingWindowRateLimit(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SlidingWindowRateLimitMiddleware>();
        }

        private sealed class SlidingWindowRateLimitMiddleware : IMiddleware
        {
            private readonly ICacheService _cache;
            private readonly SlidingWindowOptions _opt;

            public SlidingWindowRateLimitMiddleware(ICacheService cache, SlidingWindowOptions opt)
            {
                _cache = cache;
                _opt = opt;
            }

            public async Task InvokeAsync(HttpContext context, RequestDelegate next)
            {
                var id = context.User?.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst("sub")?.Value
                        ?? context.User.FindFirst("id")?.Value
                        ?? "auth:unknown"
                    : $"ip:{context.Connection.RemoteIpAddress}";

                var key = (RedisKey)($"{_opt.KeyPrefix}{id}");

                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var windowMs = (long)_opt.Window.TotalMilliseconds;
                var ttlMs = (long)_opt.KeyTtl.TotalMilliseconds;

                var res = await _cache.EvalAsync(
                    SlidingWindowLua,
                    new RedisKey[] { key },
                    new RedisValue[] { nowMs, windowMs, _opt.Limit, ttlMs }
                ).ConfigureAwait(false);

                int allowed;
                int used;
                if (res is int[] ia)
                {
                    allowed = ia[0];
                    used = ia[1];
                }
                else
                {
                    var arr = (RedisResult[])res;
                    allowed = (int)arr[0];
                    used = int.TryParse(arr[1].ToString(), out var c) ? c : _opt.Limit;
                }

                var remaining = Math.Max(0, _opt.Limit - used);

                context.Response.Headers["X-RateLimit-Limit"] = _opt.Limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();

                if (allowed == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    await context.Response.WriteAsync("Rate limit exceeded.").ConfigureAwait(false);
                    return;
                }

                await next(context).ConfigureAwait(false);
            }
        }
    }
}
