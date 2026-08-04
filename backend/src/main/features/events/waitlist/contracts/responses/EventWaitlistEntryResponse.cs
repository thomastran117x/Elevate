namespace backend.main.features.events.waitlist.contracts.responses
{
    /// <summary>
    /// A waitlist entry. PII fields (UserName, UserEmail, Notes, PhoneNumber, DietaryNeeds)
    /// are populated only when the caller can manage the event.
    /// </summary>
    public class EventWaitlistEntryResponse
    {
        public int Id
        {
            get; set;
        }
        public int EventId
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }

        /// <summary>1-based place in the queue. 0 for entries that are no longer Waiting.</summary>
        public int Position
        {
            get; set;
        }

        public string Status { get; set; } = string.Empty;

        public DateTime JoinedAtUtc
        {
            get; set;
        }
        public DateTime? PromotedAtUtc
        {
            get; set;
        }
        public DateTime? LeftAtUtc
        {
            get; set;
        }
        public DateTime? RemovedAtUtc
        {
            get; set;
        }

        // PII — null unless the caller can manage the event.
        public string? UserName
        {
            get; set;
        }
        public string? UserEmail
        {
            get; set;
        }
        public string? Notes
        {
            get; set;
        }
        public string? PhoneNumber
        {
            get; set;
        }
        public string? DietaryNeeds
        {
            get; set;
        }
    }
}
