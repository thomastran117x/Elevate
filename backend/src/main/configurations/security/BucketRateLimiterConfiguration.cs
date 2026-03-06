using System.Net;

using backend.main.services.interfaces;

using StackExchange.Redis;

namespace backend.main.configurations.security
{
    public sealed record TokenBucketOptions(
        int Capacity,
        double RefillPerSecond,
        int TokensPerRequest,
        TimeSpan KeyTtl,
        string KeyPrefix = "rl:tb:"
    );

    public static class TokenBucketRateLimitConfig
    {
        private const string TokenBucketLua = @"
            local tokensKey = KEYS[1]
            local tsKey = KEYS[2]

            local nowMs = tonumber(ARGV[1])
            local capacity = tonumber(ARGV[2])
            local refillPerMs = tonumber(ARGV[3])
            local cost = tonumber(ARGV[4])
            local ttlMs = tonumber(ARGV[5])

            local tokens = redis.call('GET', tokensKey)
            local lastTs = redis.call('GET', tsKey)

            if tokens == false then tokens = capacity else tokens = tonumber(tokens) end
            if lastTs == false then lastTs = nowMs else lastTs = tonumber(lastTs) end

            local delta = math.max(0, nowMs - lastTs)
            local refill = delta * refillPerMs
            tokens = math.min(capacity, tokens + refill)

            local allowed = 0
            if tokens >= cost then
            tokens = tokens - cost
            allowed = 1
            end

            redis.call('SET', tokensKey, tokens, 'PX', ttlMs)
            redis.call('SET', tsKey, nowMs, 'PX', ttlMs)

            return { allowed, tokens }
            ";

        public static IServiceCollection AddTokenBucketRateLimit(
            this IServiceCollection services,
            TokenBucketOptions options
        )
        {
            services.AddSingleton(options);
            services.AddSingleton<TokenBucketRateLimitMiddleware>();
            return services;
        }

        public static IApplicationBuilder UseTokenBucketRateLimit(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TokenBucketRateLimitMiddleware>();
        }

        private sealed class TokenBucketRateLimitMiddleware : IMiddleware
        {
            private readonly ICacheService _cache;
            private readonly TokenBucketOptions _opt;

            public TokenBucketRateLimitMiddleware(ICacheService cache, TokenBucketOptions opt)
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

                var keyBase = $"{_opt.KeyPrefix}{id}";
                var tokensKey = (RedisKey)($"{keyBase}:t");
                var tsKey = (RedisKey)($"{keyBase}:ts");

                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var refillPerMs = _opt.RefillPerSecond / 1000.0;
                var ttlMs = (long)_opt.KeyTtl.TotalMilliseconds;

                var res = await _cache.EvalAsync(
                    TokenBucketLua,
                    new RedisKey[] { tokensKey, tsKey },
                    new RedisValue[]
                    {
                        nowMs,
                        _opt.Capacity,
                        refillPerMs,
                        _opt.TokensPerRequest,
                        ttlMs
                    }
                ).ConfigureAwait(false);

                int allowed;
                string remainingStr;
                if (res is int[] ia)
                {
                    allowed = ia[0];
                    remainingStr = ia[1].ToString();
                }
                else
                {
                    var arr = (RedisResult[])res;
                    allowed = (int)arr[0];
                    remainingStr = arr[1].ToString() ?? "0";
                }
                context.Response.Headers["X-RateLimit-Limit"] = _opt.Capacity.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = remainingStr ?? "0";

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
