namespace backend.main.features.events.series;

/// <summary>How often a series repeats.</summary>
public enum EventRecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

/// <summary>What terminates a series.</summary>
public enum EventRecurrenceEndMode
{
    /// <summary>Stop after a fixed number of occurrences.</summary>
    Count = 0,

    /// <summary>Stop once the local date passes an inclusive cutoff.</summary>
    UntilDate = 1
}

/// <summary>
/// How a monthly series behaves in months too short for its anchor day — only ever relevant
/// for anchor days 29, 30 and 31.
/// </summary>
public enum EventMonthlyDayPolicy
{
    /// <summary>
    /// Fall back to the last day of the short month (the 31st becomes Feb 28/29, Apr 30).
    /// The default: a by-count series always yields exactly the number of occurrences the
    /// organizer asked for, and "end of the month" is the usual intent.
    /// </summary>
    ClampToMonthEnd = 0,

    /// <summary>
    /// Skip the month entirely, matching RFC 5545 BYMONTHDAY. A "31st of the month" series
    /// then runs only in the seven months that have a 31st.
    /// </summary>
    SkipMissingMonths = 1
}

/// <summary>Lifecycle of the series itself, independent of its occurrences' states.</summary>
public enum EventSeriesStatus
{
    Active = 0,
    Cancelled = 1
}

/// <summary>How much a series delete should take with it.</summary>
public enum EventSeriesDeleteScope
{
    /// <summary>Detach every occurrence and delete only the series row.</summary>
    SeriesRecordOnly = 0,

    /// <summary>Delete future drafts that nobody has registered for; detach the rest.</summary>
    FutureDrafts = 1,

    /// <summary>Delete every occurrence with no registrations; detach the rest.</summary>
    AllUnregistered = 2
}
