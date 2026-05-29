namespace backend.main.features.events.registration
{
    public class EventRegistration
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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public RegistrationStatus Status { get; set; } = RegistrationStatus.Active;
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
    }
}
