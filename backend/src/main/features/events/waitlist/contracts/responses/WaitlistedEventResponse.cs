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

        /// <summary>
        /// True when the user can no longer view this event (e.g. a private-event invitation was
        /// revoked after they queued). <see cref="Event"/> is then redacted down to its id, but
        /// the row is still returned so the user retains a way to withdraw and have their stored
        /// contact details removed — omitting it entirely would strand them in the queue.
        /// </summary>
        public bool AccessRevoked
        {
            get; set;
        }

        public EventResponse Event { get; set; } = null!;
    }
}
