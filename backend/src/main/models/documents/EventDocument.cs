using Elastic.Clients.Elasticsearch;

namespace backend.main.models.documents
{
    public class EventDocument
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public bool IsPrivate { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string Category { get; set; } = string.Empty;
        public string? VenueName { get; set; }
        public string? City { get; set; }
        public List<string> Tags { get; set; } = new();
        public GeoLocation? LocationGeo { get; set; }
        public int RegistrationCount { get; set; }
    }
}
