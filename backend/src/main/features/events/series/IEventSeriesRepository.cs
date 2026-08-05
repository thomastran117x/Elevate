namespace backend.main.features.events.series;

/// <summary>
/// Read access to series rows and their occurrences. Mutating paths go through the service's
/// DbContext directly so they participate in its transaction, matching the waitlist feature.
/// </summary>
public interface IEventSeriesRepository
{
    Task<EventSeries?> GetByIdAsync(int seriesId);

    Task<(IReadOnlyList<EventSeries> Series, int TotalCount)> GetByClubAsync(
        int clubId,
        int page,
        int pageSize);

    /// <summary>Occurrences in schedule order, images included, no change tracking.</summary>
    Task<IReadOnlyList<Events>> GetOccurrencesAsync(int seriesId);
}
