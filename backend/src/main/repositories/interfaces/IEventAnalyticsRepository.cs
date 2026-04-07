namespace backend.main.repositories.interfaces
{
    public interface IEventAnalyticsRepository
    {
        Task<EventAnalyticsData> GetEventAnalyticsAsync(int eventId);
        Task<ClubAnalyticsData> GetClubAnalyticsAsync(int clubId);
    }

    public record EventAnalyticsData(
        int RegistrationCount,
        int RegistrationsToday,
        int RegistrationsThisWeek,
        int RegistrationsThisMonth,
        long TotalRevenue,
        long PendingRevenue,
        long RefundedAmount
    );

    public record PerEventAnalytics(
        int EventId,
        string EventName,
        int MaxParticipants,
        int RegistrationCount,
        long Revenue
    );

    public record ClubAnalyticsData(
        int TotalEvents,
        int UpcomingEvents,
        int OngoingEvents,
        int PastEvents,
        int TotalRegistrations,
        long TotalRevenue,
        long PendingRevenue,
        List<PerEventAnalytics> PerEvent,
        List<(DateOnly Date, int Count)> DailyTrend
    );
}
