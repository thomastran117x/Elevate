using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.series;

public class EventSeriesRepository : IEventSeriesRepository
{
    private readonly AppDatabaseContext _db;

    public EventSeriesRepository(AppDatabaseContext db)
    {
        _db = db;
    }

    public async Task<EventSeries?> GetByIdAsync(int seriesId) =>
        await _db.EventSeries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == seriesId);

    public async Task<(IReadOnlyList<EventSeries> Series, int TotalCount)> GetByClubAsync(
        int clubId,
        int page,
        int pageSize)
    {
        var query = _db.EventSeries
            .AsNoTracking()
            .Where(s => s.ClubId == clubId);

        var totalCount = await query.CountAsync();

        var series = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (series, totalCount);
    }

    public async Task<IReadOnlyList<Events>> GetOccurrencesAsync(int seriesId) =>
        await _db.Events
            .AsNoTracking()
            .Include(e => e.Images)
            .Where(e => e.SeriesId == seriesId)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync();
}
