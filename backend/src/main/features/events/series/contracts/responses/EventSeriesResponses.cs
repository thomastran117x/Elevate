using backend.main.features.events.contracts.responses;

namespace backend.main.features.events.series.contracts.responses;

/// <summary>One generated occurrence, before or after it has been persisted.</summary>
public class EventOccurrencePreviewResponse
{
    public int Index
    {
        get; set;
    }

    /// <summary>Wall-clock start in the series' zone, as <c>yyyy-MM-ddTHH:mm:ss</c> with no offset.</summary>
    public string LocalStart { get; set; } = string.Empty;

    public DateTime StartTimeUtc
    {
        get; set;
    }

    public DateTime? EndTimeUtc
    {
        get; set;
    }

    /// <summary>The zone's offset at this instant, e.g. <c>-05:00</c>. Shifts across a DST boundary.</summary>
    public string UtcOffset { get; set; } = string.Empty;

    /// <summary>The requested wall-clock time did not exist and was moved forward out of the gap.</summary>
    public bool WasInvalidLocalTime
    {
        get; set;
    }

    /// <summary>The requested wall-clock time happened twice; the earlier instant was used.</summary>
    public bool WasAmbiguousLocalTime
    {
        get; set;
    }
}

/// <summary>Dry-run expansion result.</summary>
public class EventSeriesPreviewResponse
{
    public string TimeZoneId { get; set; } = string.Empty;
    public int OccurrenceCount
    {
        get; set;
    }
    public List<EventOccurrencePreviewResponse> Occurrences { get; set; } = new();

    /// <summary>Organizer-facing notes: DST adjustments, clamped month-ends, skipped months.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>The repeat rule, echoed back.</summary>
public class EventSeriesRuleResponse
{
    public EventRecurrenceFrequency Frequency
    {
        get; set;
    }
    public int Interval
    {
        get; set;
    }
    public List<DayOfWeek> ByWeekdays { get; set; } = new();
    public EventMonthlyDayPolicy MonthlyDayPolicy
    {
        get; set;
    }
    public string TimeZoneId { get; set; } = string.Empty;
    public string FirstOccurrenceLocalStart { get; set; } = string.Empty;
    public int? DurationMinutes
    {
        get; set;
    }
    public EventRecurrenceEndMode EndMode
    {
        get; set;
    }
    public string? EndLocalDate
    {
        get; set;
    }
    public int? OccurrenceCount
    {
        get; set;
    }
}

/// <summary>A series with its materialized occurrences.</summary>
public class EventSeriesResponse
{
    public int Id
    {
        get; set;
    }
    public int ClubId
    {
        get; set;
    }
    public int? TemplateEventId
    {
        get; set;
    }
    public EventSeriesStatus Status
    {
        get; set;
    }
    public int GeneratedCount
    {
        get; set;
    }
    public EventSeriesRuleResponse Rule { get; set; } = new();
    public DateTime CreatedAt
    {
        get; set;
    }
    public DateTime UpdatedAt
    {
        get; set;
    }

    /// <summary>Occurrences in schedule order, shaped exactly like any other managed event.</summary>
    public List<ManagedEventResponse> Occurrences { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

/// <summary>Series without its occurrences, for list views.</summary>
public class EventSeriesSummaryResponse
{
    public int Id
    {
        get; set;
    }
    public int ClubId
    {
        get; set;
    }
    public int? TemplateEventId
    {
        get; set;
    }
    public EventSeriesStatus Status
    {
        get; set;
    }
    public int GeneratedCount
    {
        get; set;
    }
    public EventSeriesRuleResponse Rule { get; set; } = new();
    public DateTime CreatedAt
    {
        get; set;
    }
    public DateTime UpdatedAt
    {
        get; set;
    }

    /// <summary>Name of the first occurrence, so a list has something human to show.</summary>
    public string? Name
    {
        get; set;
    }

    public DateTime? NextOccurrenceUtc
    {
        get; set;
    }
}

/// <summary>An occurrence a bulk operation deliberately left alone, and why.</summary>
public class EventSeriesSkippedOccurrence
{
    public int EventId
    {
        get; set;
    }
    public int? OccurrenceIndex
    {
        get; set;
    }

    /// <summary>Stable machine-readable code, e.g. <c>capacity-below-registrations</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Human-readable explanation, safe to surface directly in the UI.</summary>
    public List<string> Details { get; set; } = new();
}

/// <summary>
/// Outcome of a bulk series operation. Partial success is the normal case, not an error: one
/// occurrence that cannot be repriced should not block the other eleven from being updated.
/// </summary>
public class EventSeriesBulkResultResponse
{
    public int SeriesId
    {
        get; set;
    }
    public int AffectedCount
    {
        get; set;
    }
    public List<int> AffectedEventIds { get; set; } = new();
    public List<EventSeriesSkippedOccurrence> Skipped { get; set; } = new();

    /// <summary>
    /// Occurrences that were retimed while holding registrations, so the UI can offer to notify
    /// the people who had already signed up.
    /// </summary>
    public List<int> RetimedWithRegistrations { get; set; } = new();
}
