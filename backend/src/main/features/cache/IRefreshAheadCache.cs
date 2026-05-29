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
            double refreshThresholdPercent = 0.2)
            where T : class;

        /// <summary>
        /// Removes the entry from both the cache and the local expiry tracker.
        /// </summary>
        Task RemoveAsync(string key);
    }
}
