using backend.main.features.events.favourites.contracts.responses;

namespace backend.main.features.events.favourites
{
    public interface IEventFavouriteService
    {
        /// <summary>
        /// Stars an event for the user. Idempotent — starring an already-starred event succeeds
        /// and returns the existing row rather than throwing a conflict, because a star is a
        /// double-tappable control and an error on a repeat click would be noise.
        /// </summary>
        Task<EventFavouriteResponse> FavouriteAsync(int eventId, int userId, string userRole);

        /// <summary>
        /// Removes the user's star. Idempotent — unstarring something that is not starred is a
        /// no-op, for the same reason as <see cref="FavouriteAsync"/>.
        /// </summary>
        Task UnfavouriteAsync(int eventId, int userId);

        /// <summary>
        /// The caller's star on one event, carrying the same timestamp the POST and the pinned
        /// list report. <see cref="EventFavouriteResponse.FavouritedAtUtc"/> is null only when
        /// the event is not starred.
        /// </summary>
        Task<EventFavouriteResponse> GetMyStatusAsync(int eventId, int userId);

        Task<IReadOnlyList<int>> GetFavouriteEventIdsAsync(int userId);

        /// <summary>
        /// The union of the user's active registrations and their stars, ordered registered-first
        /// then by start time.
        /// </summary>
        Task<IReadOnlyList<PinnedEventResponse>> GetMyPinnedAsync(int userId, string userRole);
    }
}
