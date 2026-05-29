namespace backend.main.features.events.registration.contracts.responses
{
    public class EventRegistrationResponse
    {
        public int Id
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public int EventId
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public string Status
        {
            get; set;
        }
        public DateTime? CancelledAt
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

        public EventRegistrationResponse() { Status = string.Empty; }

        public EventRegistrationResponse(
            int id,
            int userId,
            int eventId,
            DateTime createdAt,
            RegistrationStatus status,
            DateTime? cancelledAt = null,
            string? notes = null,
            string? phoneNumber = null,
            string? dietaryNeeds = null)
        {
            Id = id;
            UserId = userId;
            EventId = eventId;
            CreatedAt = createdAt;
            Status = status.ToString();
            CancelledAt = cancelledAt;
            Notes = notes;
            PhoneNumber = phoneNumber;
            DietaryNeeds = dietaryNeeds;
        }
    }
}
