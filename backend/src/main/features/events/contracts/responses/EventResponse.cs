using System.Text.Json.Serialization;

using backend.main.features.events;

namespace backend.main.features.events.contracts.responses
{
    public class EventHostClubResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClubType { get; set; } = string.Empty;
        public string ClubImage { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int EventCount { get; set; }
        public int AvailableEventCount { get; set; }
        public bool IsPrivate { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public double? Rating { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? Location { get; set; }
    }

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

        // Populated only when a proximity search was performed.
        public double? DistanceKm { get; set; }

        // Populated on the event detail endpoint.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EventHostClubResponse? Club { get; set; }
    }
}


