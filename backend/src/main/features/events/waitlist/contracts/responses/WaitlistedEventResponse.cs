using backend.main.features.events.contracts.responses;

namespace backend.main.features.events.waitlist.contracts.responses
{
    /// <summary>An event the current user is queued for, with their place in line.</summary>
    public class WaitlistedEventResponse
    {
        public int EntryId
        {
            get; set;
        }
        public int Position
        {
            get; set;
        }
        public DateTime JoinedAtUtc
        {
            get; set;
        }
        public EventResponse Event { get; set; } = null!;
    }
}
