namespace backend.main.features.events.favourites.contracts.responses
{
    /// <summary>The current user's star on an event.</summary>
    public class EventFavouriteResponse
    {
        public int EventId
        {
            get; set;
        }
        public bool IsFavourited
        {
            get; set;
        }
        public DateTime FavouritedAtUtc
        {
            get; set;
        }
    }
}
