using backend.main.features.events.contracts.responses;

namespace backend.main.features.events.favourites.contracts.responses
{
    /// <summary>
    /// One row of the user's pinned list — the union of the events they registered for and the
    /// events they starred. <see cref="IsRegistered"/> and <see cref="IsFavourited"/> say which
    /// signal (or both) put it here; the client groups on <see cref="IsRegistered"/>.
    /// </summary>
    public class PinnedEventResponse
    {
        public bool IsRegistered
        {
            get; set;
        }
        public bool IsFavourited
        {
            get; set;
        }
        public DateTime? FavouritedAtUtc
        {
            get; set;
        }
        public DateTime? RegisteredAtUtc
        {
            get; set;
        }

        /// <summary>
        /// True when the user can no longer view this event (e.g. a private-event invitation was
        /// revoked after they starred it). <see cref="Event"/> is then redacted down to its id,
        /// but the row is still returned so the user retains a way to unstar it — this page is
        /// the only place the UI offers that, so omitting the row would strand the star forever.
        /// </summary>
        public bool AccessRevoked
        {
            get; set;
        }

        public EventResponse Event { get; set; } = null!;
    }
}
