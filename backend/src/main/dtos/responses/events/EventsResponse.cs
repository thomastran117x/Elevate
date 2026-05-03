using backend.main.models.enums;

namespace backend.main.dtos.responses.events
{
    public class EventResponse
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Location { get; set; } = null!;
        public List<string> ImageUrls { get; set; } = new();
        public bool IsPrivate
        {
            get; set;
        }
        public int MaxParticipants
        {
            get; set;
        }
        public int RegisterCost
        {
            get; set;
        }
        public DateTime StartTime
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
        public DateTime CreatedAt
        {
            get; set;
        }
        public EventStatus Status { get; set; }

        public EventCategory Category { get; set; }
        public string? VenueName { get; set; }
        public string? City { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public List<string> Tags { get; set; } = new();
        public int RegistrationCount { get; set; }
        public string Source { get; set; } = general.ResponseSource.Database;

        // Populated only when a proximity search was performed.
        public double? DistanceKm { get; set; }
    }
}
