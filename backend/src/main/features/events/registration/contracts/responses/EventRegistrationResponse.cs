namespace backend.main.features.events.registration.contracts.responses
{
    public class EventRegistrationResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int EventId { get; set; }
        public DateTime CreatedAt { get; set; }

        public EventRegistrationResponse(int id, int userId, int eventId, DateTime createdAt)
        {
            Id = id;
            UserId = userId;
            EventId = eventId;
            CreatedAt = createdAt;
        }
    }
}

