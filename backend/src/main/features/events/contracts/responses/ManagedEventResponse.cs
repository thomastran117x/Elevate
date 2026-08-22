namespace backend.main.features.events.contracts.responses
{
    public sealed class ManagedEventResponse
    {
        public int Id
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public string? Description
        {
            get; set;
        }
        public string? Location
        {
            get; set;
        }
        public List<string> ImageUrls { get; set; } = new();
        public bool IsPrivate
        {
            get; set;
        }
        public int? MaxParticipants
        {
            get; set;
        }
        public int RegisterCost
        {
            get; set;
        }
        public DateTime? StartTime
        {
            get; set;
        }
        public DateTime? EndTime
        {
            get; set;
        }
        public int ClubId
        {
            get; set;
        }
        public int CurrentVersionNumber
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public DateTime UpdatedAt
        {
            get; set;
        }
        public EventStatus? Status
        {
            get; set;
        }
        public EventLifecycleState LifecycleState
        {
            get; set;
        }
        public EventCategory Category
        {
            get; set;
        }
        public string? VenueName
        {
            get; set;
        }
        public string? City
        {
            get; set;
        }
        public double? Latitude
        {
            get; set;
        }
        public double? Longitude
        {
            get; set;
        }
        public List<string> Tags { get; set; } = new();
        public int RegistrationCount
        {
            get; set;
        }
        public bool WaitlistEnabled
        {
            get; set;
        }
        public int WaitlistCount
        {
            get; set;
        }
        public int? SeriesId
        {
            get; set;
        }
        public int? OccurrenceIndex
        {
            get; set;
        }

        /// <summary>
        /// True once this occurrence has been edited on its own, which excludes it from
        /// "update all future occurrences" unless the organizer opts back in.
        /// </summary>
        public bool SeriesOverridden
        {
            get; set;
        }

        public string? TimeZoneId
        {
            get; set;
        }

        public bool PublishReady
        {
            get; set;
        }
        public List<string> PublishIssues { get; set; } = new();

        /// <summary>When <see cref="LifecycleState"/> last changed, or null if it never has.</summary>
        public DateTime? LifecycleChangedAt
        {
            get; set;
        }

        /// <summary>The state the most recent lifecycle change moved away from, if any.</summary>
        public EventLifecycleState? PreviousLifecycleState
        {
            get; set;
        }

        /// <summary>
        /// Deadline for undoing the most recent lifecycle change, or null when there is nothing
        /// to undo or the window has already lapsed. Drives the "Undo" affordance.
        /// </summary>
        public DateTime? RevertAvailableUntil
        {
            get; set;
        }

        /// <summary>Every lifecycle move available from the current state.</summary>
        public List<EventLifecycleTransitionResponse> AvailableTransitions { get; set; } = new();
    }
}
