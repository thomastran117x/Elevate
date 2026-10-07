using backend.main.features.cache;

using StackExchange.Redis;

namespace backend.main.features.auth.abuse;

public readonly record struct AuthCounterResult(
    bool StoreAvailable,
    bool Allowed,
    long Count,
    TimeSpan? RetryAfter);

public interface IAuthAbuseProtectionStore
{
    Task<AuthCounterResult> ReserveAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    Task<AuthCounterResult> ReadAsync(
        string key,
        int limit,
        CancellationToken cancellationToken = default);

    Task<AuthCounterResult> RecordFailureAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class RedisAuthAbuseProtectionStore : IAuthAbuseProtectionStore
{
    private const string ReserveScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        if current >= limit then
            return {0, current, redis.call('PTTL', KEYS[1])}
        end
        current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], window)
        end
        return {1, current, redis.call('PTTL', KEYS[1])}
        """;

    private const string ReadScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        local limit = tonumber(ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if current >= limit then
            return {0, current, ttl}
        end
        return {1, current, ttl}
        """;

    private const string RecordFailureScript = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        if current < limit then
            current = redis.call('INCR', KEYS[1])
            if current == 1 then
                redis.call('PEXPIRE', KEYS[1], window)
            end
        end
        local allowed = 1
        if current >= limit then allowed = 0 end
        return {allowed, current, redis.call('PTTL', KEYS[1])}
        """;

    private const string DeleteScript = "return redis.call('DEL', KEYS[1])";

    private readonly ICacheService _cache;

    public RedisAuthAbuseProtectionStore(ICacheService cache)
    {
        _cache = cache;
    }

    public Task<AuthCounterResult> ReserveAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default) =>
        EvaluateCounterAsync(ReserveScript, key, limit, window, cancellationToken);

    public Task<AuthCounterResult> ReadAsync(
        string key,
        int limit,
        CancellationToken cancellationToken = default) =>
        EvaluateCounterAsync(ReadScript, key, limit, null, cancellationToken);

    public Task<AuthCounterResult> RecordFailureAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default) =>
        EvaluateCounterAsync(RecordFailureScript, key, limit, window, cancellationToken);

    public async Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _cache.TryEvalAsync(
            DeleteScript,
            [(RedisKey)key],
            []
        );
        return result.Succeeded;
    }

    private async Task<AuthCounterResult> EvaluateCounterAsync(
        string script,
        string key,
        int limit,
        TimeSpan? window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = window.HasValue
            ? new RedisValue[] { limit, checked((long)window.Value.TotalMilliseconds) }
            : new RedisValue[] { limit };
        var result = await _cache.TryEvalAsync(script, [(RedisKey)key], values);

        if (!result.Succeeded || result.Value is null)
            return new AuthCounterResult(false, true, 0, null);

        var parts = ReadParts(result.Value);
        TimeSpan? retryAfter = parts.TtlMilliseconds > 0
            ? TimeSpan.FromMilliseconds(parts.TtlMilliseconds)
            : null;
        return new AuthCounterResult(
            true,
            parts.Allowed != 0,
            parts.Count,
            retryAfter);
    }

    private static (long Allowed, long Count, long TtlMilliseconds) ReadParts(object value)
    {
        if (value is RedisResult result)
        {
            var parts = (RedisResult[]?)result
                ?? throw new InvalidOperationException("Redis returned an invalid abuse-counter result.");
            return ReadRedisParts(parts);
        }

        if (value is RedisResult[] redis)
            return ReadRedisParts(redis);

        if (value is int[] ints)
            return (ints[0], ints[1], ints[2]);
        if (value is long[] longs)
            return (longs[0], longs[1], longs[2]);

        throw new InvalidOperationException("Redis returned an invalid abuse-counter result.");
    }

    private static (long Allowed, long Count, long TtlMilliseconds) ReadRedisParts(
        RedisResult[] parts) =>
        (
            (long)parts[0],
            (long)parts[1],
            (long)parts[2]
        );
}
