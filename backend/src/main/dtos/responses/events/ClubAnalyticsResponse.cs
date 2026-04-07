namespace backend.main.dtos.responses.events
{
    public class ClubAnalyticsResponse
    {
        public int ClubId { get; set; }
        public int TotalEvents { get; set; }
        public int UpcomingEvents { get; set; }
        public int OngoingEvents { get; set; }
        public int PastEvents { get; set; }
        public int TotalRegistrations { get; set; }
        /// <summary>Total revenue in cents from succeeded payments across all club events.</summary>
        public long TotalRevenue { get; set; }
        /// <summary>Revenue in cents from pending payments across all club events.</summary>
        public long PendingRevenue { get; set; }
        /// <summary>Average fill rate across all club events as a percentage (0.0–100.0).</summary>
        public double AvgFillRate { get; set; }
        public List<TopEventEntry> TopEventsByRegistrations { get; set; } = new();
        public List<DailyRegistrationEntry> RegistrationTrend { get; set; } = new();
    }

    public class TopEventEntry
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int RegistrationCount { get; set; }
        public double FillRate { get; set; }
        /// <summary>Revenue in cents from succeeded payments for this event.</summary>
        public long Revenue { get; set; }
    }

    public class DailyRegistrationEntry
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
    }
}
