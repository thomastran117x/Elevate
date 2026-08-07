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
        /// <summary>
        /// Null when <see cref="IsFavourited"/> is false. Nullable rather than defaulted so a
        /// "not favourited" status does not serialize an impossible year-0001 timestamp.
        /// </summary>
        public DateTime? FavouritedAtUtc
        {
            get; set;
        }
    }
}
