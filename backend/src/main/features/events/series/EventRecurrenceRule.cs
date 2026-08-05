namespace backend.main.features.events.series;

/// <summary>
/// A repeat rule in its expandable form: pure data, no persistence concerns.
/// </summary>
/// <param name="FirstOccurrenceLocalStart">
/// Wall-clock start of occurrence 0. Must be <see cref="DateTimeKind.Unspecified"/> — a UTC or
/// local <c>Kind</c> here means a caller has already lost the wall-clock intent.
/// </param>
public sealed record EventRecurrenceRule(
    EventRecurrenceFrequency Frequency,
    int Interval,
    DateTime FirstOccurrenceLocalStart,
    int? DurationMinutes,
    IReadOnlyList<DayOfWeek>? ByWeekdays,
    EventMonthlyDayPolicy MonthlyDayPolicy,
    EventRecurrenceEndMode EndMode,
    DateOnly? EndLocalDate,
    int? OccurrenceCount,
    string TimeZoneId);

/// <summary>One generated occurrence, in both wall-clock and absolute terms.</summary>
/// <param name="LocalStart">
/// The wall-clock start actually used, after any DST-gap adjustment — not necessarily the time
/// the rule literally asked for.
/// </param>
/// <param name="LocalStartWasInvalid">
/// The requested wall-clock time did not exist (clocks sprang forward through it) and was moved
/// forward out of the gap.
/// </param>
/// <param name="LocalStartWasAmbiguous">
/// The requested wall-clock time occurred twice (clocks fell back over it); the earlier of the
/// two instants was used.
/// </param>
public sealed record EventOccurrenceSlot(
    int Index,
    DateTime LocalStart,
    DateTime StartUtc,
    DateTime? EndUtc,
    bool LocalStartWasInvalid,
    bool LocalStartWasAmbiguous);

/// <summary>Expansion result, plus any organizer-facing notes about how it was resolved.</summary>
public sealed record EventRecurrenceExpansion(
    IReadOnlyList<EventOccurrenceSlot> Occurrences,
    IReadOnlyList<string> Warnings);
