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
            double refreshThresholdPercent = 0.2,
            JsonSerializerOptions? serializerOptions = null)
            where T : class
        {
            var sentinelTtl = nullSentinelTtl ?? DefaultNullSentinelTtl;
            var cached = await _cache.GetValueAsync(key);

            if (cached == NullSentinel)
                return null;

            if (cached != null)
            {
                var entity = Deserialize<T>(cached, serializerOptions);
                MaybeRefreshAhead(key, factory, ttl, sentinelTtl, refreshThresholdPercent, serializerOptions);
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
            await _cache.SetValueAsync(key, Serialize(fetched, serializerOptions), jittered);
            _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
            return fetched;
        }

        public async Task<TEntity?> GetOrSetAsync<TEntity, TStored>(
            string key,
            Func<Task<TEntity?>> factory,
            Func<TEntity, TStored> toStored,
            Func<TStored, TEntity> fromStored,
            TimeSpan ttl,
            TimeSpan? nullSentinelTtl = null,
            double refreshThresholdPercent = 0.2)
            where TEntity : class
            where TStored : class
        {
            var sentinelTtl = nullSentinelTtl ?? DefaultNullSentinelTtl;
            var cached = await _cache.GetValueAsync(key);

            if (cached == NullSentinel)
                return null;

            if (cached != null)
            {
                var stored = JsonSerializer.Deserialize<TStored>(cached)!;
                var entity = fromStored(stored);
                MaybeRefreshAheadMapped(key, factory, toStored, ttl, sentinelTtl, refreshThresholdPercent);
                return entity;
            }

            var fetched = await factory();

            if (fetched == null)
            {
                await _cache.SetValueAsync(key, NullSentinel, sentinelTtl);
                _expiryTicks.TryRemove(key, out _);
                return null;
            }

            var jittered = WithJitter(ttl);
            await _cache.SetValueAsync(key, JsonSerializer.Serialize(toStored(fetched)), jittered);
            _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
            return fetched;
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, JsonSerializerOptions? serializerOptions = null)
            where T : class
        {
            var jittered = WithJitter(ttl);
            await _cache.SetValueAsync(key, Serialize(value, serializerOptions), jittered);
            _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
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
            double refreshThresholdPercent,
            JsonSerializerOptions? serializerOptions)
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
                        await _cache.SetValueAsync(key, Serialize(fresh, serializerOptions), jittered);
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

        private void MaybeRefreshAheadMapped<TEntity, TStored>(
            string key,
            Func<Task<TEntity?>> factory,
            Func<TEntity, TStored> toStored,
            TimeSpan ttl,
            TimeSpan sentinelTtl,
            double refreshThresholdPercent)
            where TEntity : class
            where TStored : class
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
                        await _cache.SetValueAsync(key, JsonSerializer.Serialize(toStored(fresh)), jittered);
                        _expiryTicks[key] = DateTime.UtcNow.Add(jittered).Ticks;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Refresh-ahead (mapped) failed for cache key '{key}'");
                }
                finally
                {
                    _refreshing.TryRemove(key, out _);
                }
            });
        }

        private static string Serialize<T>(T value, JsonSerializerOptions? options) =>
            options == null
                ? JsonSerializer.Serialize(value)
                : JsonSerializer.Serialize(value, options);

        private static T Deserialize<T>(string json, JsonSerializerOptions? options) =>
            options == null
                ? JsonSerializer.Deserialize<T>(json)!
                : JsonSerializer.Deserialize<T>(json, options)!;

        private static TimeSpan WithJitter(TimeSpan baseTtl, int percent = 20)
        {
            var delta = Random.Shared.Next(-percent, percent + 1);
            return baseTtl + TimeSpan.FromMilliseconds(baseTtl.TotalMilliseconds * delta / 100.0);
        }
    }
}
