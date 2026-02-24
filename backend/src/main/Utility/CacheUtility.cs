using System.Text.Json;

using backend.main.Interfaces;

namespace backend.main.Services
{
    public static class CacheHelpers
    {
        public static async Task<T?> GetOrSetAsync<T>(
            ICacheService cache,
            string key,
            Func<Task<T>> factory,
            TimeSpan ttl
        )
        {
            var cached = await cache.GetValueAsync(key);
            if (!string.IsNullOrWhiteSpace(cached))
                return JsonSerializer.Deserialize<T>(cached);

            var lockKey = $"{key}:lock";
            var lockId = Guid.NewGuid().ToString();

            if (await cache.AcquireLockAsync(lockKey, lockId, TimeSpan.FromSeconds(5)))
            {
                try
                {
                    var value = await factory();
                    await cache.SetValueAsync(
                        key,
                        JsonSerializer.Serialize(value),
                        ttl
                    );
                    return value;
                }
                finally
                {
                    await cache.ReleaseLockAsync(lockKey, lockId);
                }
            }

            await Task.Delay(50);

            cached = await cache.GetValueAsync(key);
            return cached is null ? default : JsonSerializer.Deserialize<T>(cached);
        }
    }

    public static class CacheRefreshPolicy
    {
        public static bool ShouldRefresh(
            TimeSpan? ttl,
            TimeSpan refreshAhead)
        {
            if (!ttl.HasValue)
                return true;

            if (ttl.Value <= TimeSpan.Zero)
                return true;

            return ttl.Value <= refreshAhead;
        }
    }
}
