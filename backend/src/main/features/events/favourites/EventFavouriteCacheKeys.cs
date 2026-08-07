using backend.main.features.cache;

namespace backend.main.features.events.favourites
{
    /// <summary>
    /// Favourite cache key builders and invalidation.
    /// <para>
    /// Note there is deliberately no per-event lock here, unlike
    /// <see cref="registration.EventRegistrationCacheKeys.Lock"/>. Starring does not change an
    /// event's seat count, so it has nothing to serialize against — and taking that lock would
    /// risk a favourite write blocking a registration.
    /// </para>
    /// </summary>
    public static class EventFavouriteCacheKeys
    {
        public static string Ids(int userId) => $"favevt:ids:u:{userId}";

        public static string Pinned(int userId) => $"favevt:pinned:u:{userId}";

        /// <summary>Drops both per-user projections after a star or unstar.</summary>
        public static async Task InvalidateUserAsync(IRefreshAheadCache cache, int userId)
        {
            await cache.RemoveAsync(Ids(userId));
            await cache.RemoveAsync(Pinned(userId));
        }
    }
}
