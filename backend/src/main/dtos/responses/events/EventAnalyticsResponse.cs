namespace backend.main.dtos.responses.events
{
    public class EventAnalyticsResponse
    {
        public int EventId { get; set; }
        public string EventName { get; set; } = null!;
        public int RegistrationCount { get; set; }
        public int MaxParticipants { get; set; }
        /// <summary>Fill rate as a percentage (0.0–100.0).</summary>
        public double FillRate { get; set; }
        public int SpotsRemaining { get; set; }
        /// <summary>Total revenue in cents from succeeded payments.</summary>
        public long TotalRevenue { get; set; }
        /// <summary>Revenue in cents from pending payments.</summary>
        public long PendingRevenue { get; set; }
        /// <summary>Total amount in cents from refunded payments.</summary>
        public long RefundedAmount { get; set; }
        public int RegistrationsToday { get; set; }
        public int RegistrationsThisWeek { get; set; }
        public int RegistrationsThisMonth { get; set; }
    }
}
