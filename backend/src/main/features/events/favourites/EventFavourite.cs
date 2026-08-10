namespace backend.main.features.events.favourites
{
    /// <summary>
    /// A user's lightweight "saved for later" star on an event. Deliberately minimal: unlike
    /// <c>EventRegistration</c> there is no PII, no capacity accounting and no audit need, so
    /// unstarring hard-deletes the row rather than soft-cancelling it.
    /// </summary>
    public class EventFavourite
    {
        public int Id
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public int EventId
        {
            get; set;
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
