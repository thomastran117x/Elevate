namespace backend.main.features.events.registration
{
    public class EventRegistration
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int EventId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}


