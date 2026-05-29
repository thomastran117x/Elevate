using System.Collections.Concurrent;
using System.Text.Json;

using backend.main.shared.utilities.logger;

namespace backend.main.features.cache
{
    public class RefreshAheadCache : IRefreshAheadCache
    {
        private const string NullSentinel = "__null__";
        private static readonly TimeSpan DefaultNullSentinelTtl = TimeSpan.FromSeconds(15);

        private readonly ICacheService _cache;

        // Tracks when each key is expected to expire (UTC ticks) so we can decide
        // whether to trigger a background refresh without an extra Redis round-trip.
        private readonly ConcurrentDictionary<string, long> _expiryTicks = new();

        // Prevents concurrent background refreshes for the same key.
        private readonly ConcurrentDictionary<string, byte> _refreshing = new();

        public RefreshAheadCache(ICacheService cache)
        {
            _cache = cache;
        }

        public async Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T?>> factory,
            TimeSpan ttl,
            TimeSpan? nullSentinelTtl = null,
            double refreshThresholdPercent = 0.2)
            where T : class
        {
            var sentinelTtl = nullSentinelTtl ?? DefaultNullSentinelTtl;
            var cached = await _cache.GetValueAsync(key);

            if (cached == NullSentinel)
                return null;

            if (cached != null)
            {
                var entity = JsonSerializer.Deserialize<T>(cached)!;
                MaybeRefreshAhead(key, factory, ttl, sentinelTtl, refreshThresholdPercent);
                return entity;
            }

            // Cache miss — fetch from source
            var fetched = await factory();

            if (fetched == null)
            {
                await _cache.SetValueAsync(key, NullSentinel, sentinelTtl);
                _expiryTicks.TryRemove(key, out _);
                return null;
            }

            var jittered = WithJitter(ttl);
            await _cache.SetValueAsync(key, JsonSerializer.Serialize(fetched), jittered);
            _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
            return fetched;
        }

        public async Task RemoveAsync(string key)
        {
            _expiryTicks.TryRemove(key, out _);
            await _cache.DeleteKeyAsync(key);
        }

        private void MaybeRefreshAhead<T>(
            string key,
            Func<Task<T?>> factory,
            TimeSpan ttl,
            TimeSpan sentinelTtl,
            double refreshThresholdPercent)
            where T : class
        {
            if (!_expiryTicks.TryGetValue(key, out var expiryTick))
                return;

            var remainingMs = (new DateTime(expiryTick, DateTimeKind.Utc) - DateTime.UtcNow).TotalMilliseconds;
            var thresholdMs = ttl.TotalMilliseconds * refreshThresholdPercent;

            if (remainingMs > thresholdMs)
                return;

            if (!_refreshing.TryAdd(key, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var fresh = await factory();

                    if (fresh == null)
                    {
                        await _cache.SetValueAsync(key, NullSentinel, sentinelTtl);
                        _expiryTicks.TryRemove(key, out _);
                    }
                    else
                    {
                        var jittered = WithJitter(ttl);
                        await _cache.SetValueAsync(key, JsonSerializer.Serialize(fresh), jittered);
                        _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Refresh-ahead failed for cache key '{key}'");
                }
                finally
                {
                    _refreshing.TryRemove(key, out _);
                }
            });
        }

        private static TimeSpan WithJitter(TimeSpan baseTtl, int percent = 20)
        {
            var delta = Random.Shared.Next(-percent, percent + 1);
            return baseTtl + TimeSpan.FromMilliseconds(baseTtl.TotalMilliseconds * delta / 100.0);
        }
    }
}
