using backend.main.features.cache;

namespace backend.main.features.events.registration
{
    /// <summary>
    /// Registration cache key builders and invalidation, shared between
    /// EventRegistrationService and the waitlist promoter — the promoter registers users
    /// on the service's behalf and must invalidate exactly the same keys.
    /// </summary>
    public static class EventRegistrationCacheKeys
    {
        /// <summary>
        /// Per-event mutex guarding every path that changes an event's active registration
        /// count. Held by register, unregister and waitlist join/leave/promote so those
        /// paths cannot interleave. Never hold two of these at once.
        /// </summary>
        public static string Lock(int eventId) => $"evtreg:lock:{eventId}";

        public static string Membership(int userId, int eventId) => $"evtreg:u:{userId}:e:{eventId}";

        public static string EventIndex(int eventId) => $"evtreg:index:e:{eventId}";

        public static string UserIndex(int userId) => $"evtreg:index:u:{userId}";

        /// <summary>
        /// Drops every cached list page for the event and the user via the reverse-index
        /// sets, then the index sets themselves.
        /// </summary>
        public static async Task InvalidateListsAsync(ICacheService cache, int userId, int eventId)
        {
            var eventIndexKey = EventIndex(eventId);
            var userIndexKey = UserIndex(userId);

            var eventKeys = await cache.SetMembersAsync(eventIndexKey);
            var userKeys = await cache.SetMembersAsync(userIndexKey);

            foreach (var key in eventKeys.Concat(userKeys))
                await cache.DeleteKeyAsync(key);

            await cache.DeleteKeyAsync(eventIndexKey);
            await cache.DeleteKeyAsync(userIndexKey);
        }
    }
}
