namespace backend.main.features.events;

/// <summary>
/// Cache key vocabulary shared by every service that mutates events. Extracted so the
/// recurrence series feature invalidates exactly the same keys <see cref="EventsService"/>
/// does — a series write that missed one would serve stale occurrences for the full TTL.
/// </summary>
internal static class EventCacheKeys
{
    /// <summary>Per-event entity cache, held by <c>IRefreshAheadCache</c>.</summary>
    internal static string Event(int eventId) => $"event:{eventId}";

    /// <summary>
    /// Monotonic counter mixed into every event-list cache key. Bumping it invalidates
    /// all list/search pages at once without enumerating them.
    /// </summary>
    internal const string ListVersion = "events:version";
}
