using System.Text.Json;

namespace backend.main.features.cache
{
    public interface IRefreshAheadCache
    {
        /// <summary>
        /// Returns the cached value, or fetches via <paramref name="factory"/> on a miss.
        /// Returns null when the factory returns null (stores a null sentinel internally).
        /// Proactively refreshes in the background when the remaining TTL falls below
        /// <paramref name="refreshThresholdPercent"/> × <paramref name="ttl"/>.
        /// </summary>
        Task<T?> GetOrSetAsync<T>(
            string key,
            Func<Task<T?>> factory,
            TimeSpan ttl,
            TimeSpan? nullSentinelTtl = null,
            double refreshThresholdPercent = 0.2,
            JsonSerializerOptions? serializerOptions = null)
            where T : class;

        /// <summary>
        /// Variant for when the cached representation differs from the returned type.
        /// The factory produces <typeparamref name="TEntity"/>; <paramref name="toStored"/> maps it to
        /// the serialization-safe <typeparamref name="TStored"/> before caching, and
        /// <paramref name="fromStored"/> maps back on a cache hit.
        /// </summary>
        Task<TEntity?> GetOrSetAsync<TEntity, TStored>(
            string key,
            Func<Task<TEntity?>> factory,
            Func<TEntity, TStored> toStored,
            Func<TStored, TEntity> fromStored,
            TimeSpan ttl,
            TimeSpan? nullSentinelTtl = null,
            double refreshThresholdPercent = 0.2)
            where TEntity : class
            where TStored : class;

        /// <summary>
        /// Writes a value directly to the cache (write-through after mutations).
        /// Updates the local expiry tracker so refresh-ahead works on the next read.
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan ttl, JsonSerializerOptions? serializerOptions = null)
            where T : class;

        /// <summary>
        /// Removes the entry from both the cache and the local expiry tracker.
        /// </summary>
        Task RemoveAsync(string key);
    }
}
