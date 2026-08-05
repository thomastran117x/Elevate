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
    }
}
