namespace backend.main.features.events.waitlist
{
    /// <summary>
    /// A user's place in an event's waitlist queue.
    ///
    /// Ordering is by (JoinedAtUtc, Id) over Status == Waiting; there is deliberately no
    /// stored Position column. Resequencing positions on every leave/remove/promote would
    /// produce wide range write-sets under IsolationLevel.Serializable, and this codebase
    /// has no deadlock retry policy. A computed position also cannot drift.
    ///
    /// Uniqueness is (EventId, UserId) forever — MySQL has no filtered unique indexes, so
    /// terminal entries are reactivated in place on rejoin, mirroring EventRegistration.
    /// </summary>
    public class EventWaitlistEntry
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

        public EventWaitlistEntryStatus Status { get; set; } = EventWaitlistEntryStatus.Waiting;

        /// <summary>
        /// Ordering key. Reset to UtcNow when a terminal entry is reactivated, so a rejoin
        /// goes to the back of the queue rather than reclaiming the original place.
        /// </summary>
        public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

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
        public int? RemovedByUserId
        {
            get; set;
        }

        /// <summary>
        /// Set only once the promotion email has been successfully handed to the publisher.
        /// Promotion is durable even when publishing fails, so a Promoted entry with a null
        /// marker is the only record that the user was never notified — worth a reconciliation
        /// pass, since the entry is no longer Waiting and cannot be re-driven by promotion.
        /// </summary>
        public DateTime? PromotionEmailQueuedAtUtc
        {
            get; set;
        }

        // Carried onto the EventRegistration when the entry is promoted.
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

        /// <summary>First-ever join. Immutable across reactivations, unlike JoinedAtUtc.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Events Event { get; set; } = null!;
    }
}
