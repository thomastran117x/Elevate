using backend.main.configurations.resource.database;
using backend.main.models.enums;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class EventAnalyticsRepository : IEventAnalyticsRepository
    {
        private readonly AppDatabaseContext _context;

        public EventAnalyticsRepository(AppDatabaseContext context) => _context = context;

        public async Task<EventAnalyticsData> GetEventAnalyticsAsync(int eventId)
        {
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            var weekStart = todayStart.AddDays(-(int)now.DayOfWeek);
            var monthStart = new DateTime(now.Year, now.Month, 1);

            var regStats = await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.EventId == eventId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Today = g.Count(r => r.CreatedAt >= todayStart),
                    ThisWeek = g.Count(r => r.CreatedAt >= weekStart),
                    ThisMonth = g.Count(r => r.CreatedAt >= monthStart)
                })
                .FirstOrDefaultAsync();

            var revenue = await _context.Payments
                .AsNoTracking()
                .Where(p => p.EventId == eventId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => (long?)p.Amount) ?? 0L,
                    Pending = g.Where(p => p.Status == PaymentStatus.Pending).Sum(p => (long?)p.Amount) ?? 0L,
                    Refunded = g.Where(p => p.Status == PaymentStatus.Refunded).Sum(p => (long?)p.Amount) ?? 0L
                })
                .FirstOrDefaultAsync();

            return new EventAnalyticsData(
                RegistrationCount: regStats?.Total ?? 0,
                RegistrationsToday: regStats?.Today ?? 0,
                RegistrationsThisWeek: regStats?.ThisWeek ?? 0,
                RegistrationsThisMonth: regStats?.ThisMonth ?? 0,
                TotalRevenue: revenue?.Total ?? 0L,
                PendingRevenue: revenue?.Pending ?? 0L,
                RefundedAmount: revenue?.Refunded ?? 0L
            );
        }

        public async Task<ClubAnalyticsData> GetClubAnalyticsAsync(int clubId)
        {
            var now = DateTime.UtcNow;
            var sevenDaysAgo = now.Date.AddDays(-6);

            // 1. Event status buckets
            var eventStats = await _context.Events
                .AsNoTracking()
                .Where(e => e.ClubId == clubId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Upcoming = g.Count(e => e.StartTime > now),
                    Ongoing = g.Count(e => e.StartTime <= now && (e.EndTime == null || e.EndTime > now)),
                    Past = g.Count(e => e.EndTime != null && e.EndTime <= now)
                })
                .FirstOrDefaultAsync();

            // 2. Per-event registration counts + revenue
            var perEvent = await _context.Events
                .AsNoTracking()
                .Where(e => e.ClubId == clubId)
                .Select(e => new PerEventAnalytics(
                    e.Id,
                    e.Name,
                    e.maxParticipants,
                    _context.EventRegistrations.Count(r => r.EventId == e.Id),
                    _context.Payments
                        .Where(p => p.EventId == e.Id && p.Status == PaymentStatus.Succeeded)
                        .Sum(p => (long?)p.Amount) ?? 0L
                ))
                .ToListAsync();

            // 3. Revenue totals
            var clubEventIds = perEvent.Select(p => p.EventId).ToList();

            var revenueTotals = clubEventIds.Count == 0
                ? (Total: 0L, Pending: 0L)
                : await _context.Payments
                    .AsNoTracking()
                    .Where(p => clubEventIds.Contains(p.EventId))
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        Total = g.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => (long?)p.Amount) ?? 0L,
                        Pending = g.Where(p => p.Status == PaymentStatus.Pending).Sum(p => (long?)p.Amount) ?? 0L
                    })
                    .FirstOrDefaultAsync()
                    .ContinueWith(t => t.Result == null
                        ? (Total: 0L, Pending: 0L)
                        : (Total: t.Result.Total, Pending: t.Result.Pending));

            // 4. 7-day registration trend
            var trendRaw = clubEventIds.Count == 0
                ? new List<(DateOnly, int)>()
                : await _context.EventRegistrations
                    .AsNoTracking()
                    .Where(r => clubEventIds.Contains(r.EventId) && r.CreatedAt >= sevenDaysAgo)
                    .GroupBy(r => r.CreatedAt.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .ToListAsync()
                    .ContinueWith(t => t.Result
                        .Select(x => (Date: DateOnly.FromDateTime(x.Date), Count: x.Count))
                        .ToList());

            // Backfill missing days with zero
            var trendMap = trendRaw.ToDictionary(x => x.Item1, x => x.Item2);
            var fullTrend = Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var date = DateOnly.FromDateTime(sevenDaysAgo.AddDays(i));
                    return (Date: date, Count: trendMap.GetValueOrDefault(date, 0));
                })
                .ToList();

            int totalRegistrations = perEvent.Sum(e => e.RegistrationCount);

            return new ClubAnalyticsData(
                TotalEvents: eventStats?.Total ?? 0,
                UpcomingEvents: eventStats?.Upcoming ?? 0,
                OngoingEvents: eventStats?.Ongoing ?? 0,
                PastEvents: eventStats?.Past ?? 0,
                TotalRegistrations: totalRegistrations,
                TotalRevenue: revenueTotals.Total,
                PendingRevenue: revenueTotals.Pending,
                PerEvent: perEvent,
                DailyTrend: fullTrend
            );
        }
    }
}
