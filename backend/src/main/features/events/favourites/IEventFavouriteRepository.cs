namespace backend.main.features.events.favourites
{
    /// <summary>
    /// Read-only favourite queries. Writes go through <see cref="EventFavouriteService"/> on
    /// <c>AppDatabaseContext</c> directly, matching the waitlist slice.
    /// </summary>
    public interface IEventFavouriteRepository
    {
        Task<IReadOnlyList<int>> GetEventIdsByUserAsync(int userId);

        Task<IReadOnlyList<EventFavourite>> GetByUserAsync(int userId);

        Task<bool> ExistsAsync(int eventId, int userId);
    }
}
