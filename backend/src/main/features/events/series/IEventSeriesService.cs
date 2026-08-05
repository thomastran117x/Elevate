using backend.main.features.events.series.contracts.requests;
using backend.main.features.events.series.contracts.responses;

namespace backend.main.features.events.series;

/// <summary>
/// Recurrence series operations.
/// <para>
/// Deliberately separate from <c>IEventsService</c>. Occurrences are ordinary events, so editing,
/// cancelling or deleting a single one already works through the existing event endpoints — this
/// interface only covers what is genuinely series-shaped. Keeping it apart also lets the whole
/// feature sit behind its own flag, and avoids touching the constructor of a 2,000-line service
/// whose test harness builds it positionally.
/// </para>
/// </summary>
public interface IEventSeriesService
{
    /// <summary>Expands a rule without persisting anything. Powers the wizard's preview.</summary>
    Task<EventSeriesPreviewResponse> PreviewAsync(
        int clubId,
        int userId,
        string userRole,
        EventRecurrenceRuleRequest rule);

    /// <summary>Turns an existing draft into occurrence 0 and materializes the rest as drafts.</summary>
    Task<EventSeriesResponse> CreateFromDraftAsync(
        int templateEventId,
        int userId,
        string userRole,
        CreateEventSeriesRequest request);

    Task<EventSeriesResponse> GetAsync(int seriesId, int userId, string userRole);

    Task<(IReadOnlyList<EventSeriesSummaryResponse> Series, int TotalCount)> GetByClubAsync(
        int clubId,
        int userId,
        string userRole,
        int page,
        int pageSize);

    /// <summary>Generates the occurrences a revised terminator adds, leaving existing ones alone.</summary>
    Task<EventSeriesResponse> ExtendAsync(
        int seriesId,
        int userId,
        string userRole,
        ExtendEventSeriesRequest request);

    /// <summary>Publishes every draft occurrence that passes its publish checks.</summary>
    Task<EventSeriesBulkResultResponse> PublishAsync(int seriesId, int userId, string userRole);

    /// <summary>Applies a patch to every in-scope occurrence from a pivot onward.</summary>
    Task<EventSeriesBulkResultResponse> UpdateFutureOccurrencesAsync(
        int seriesId,
        int userId,
        string userRole,
        UpdateFutureOccurrencesRequest request);

    Task<EventSeriesBulkResultResponse> CancelAsync(
        int seriesId,
        int userId,
        string userRole,
        CancelEventSeriesRequest request);

    Task<EventSeriesBulkResultResponse> DeleteAsync(
        int seriesId,
        int userId,
        string userRole,
        DeleteEventSeriesRequest request);

    /// <summary>Detaches one occurrence, leaving it as an ordinary standalone event.</summary>
    Task<Events> DetachOccurrenceAsync(int eventId, int userId, string userRole);
}
