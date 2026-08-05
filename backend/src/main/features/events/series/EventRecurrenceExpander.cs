using System.Globalization;

using backend.main.shared.exceptions.http;

namespace backend.main.features.events.series;

/// <summary>
/// Turns a repeat rule into concrete occurrence times.
/// <para>
/// The core idea: iterate entirely in <em>local wall-clock space</em> and convert each candidate
/// to UTC one at a time. Advancing a UTC instant by a fixed interval would drift by an hour
/// across a DST boundary, turning "7pm every Tuesday" into 6pm or 8pm; advancing the local date
/// and re-converting keeps the wall clock fixed, which is what an organizer means.
/// </para>
/// <para>
/// Pure and dependency-free by design, so every calendar and DST edge case is directly unit
/// testable without a database, a clock, or an HTTP request.
/// </para>
/// </summary>
public static class EventRecurrenceExpander
{
    /// <summary>Hard ceiling on occurrences generated from one rule.</summary>
    public const int MaxOccurrences = 200;

    /// <summary>Furthest a series may extend past its first occurrence (~3 years).</summary>
    public const int MaxHorizonDays = 1096;

    private const int MaxIntervalDailyWeekly = 52;
    private const int MaxIntervalMonthly = 24;

    /// <summary>
    /// Guards the <see cref="EventMonthlyDayPolicy.SkipMissingMonths"/> loop: skipped months do
    /// not advance the emitted count, so iteration needs its own bound.
    /// </summary>
    private const int MaxCandidateSteps = MaxOccurrences * 16;

    public static EventRecurrenceExpansion Expand(EventRecurrenceRule rule)
    {
        var timeZone = EventSeriesTimeZones.Resolve(rule.TimeZoneId);
        Validate(rule);

        var warnings = new List<string>();
        var occurrences = new List<EventOccurrenceSlot>();
        var clampedDates = new List<string>();
        var skippedMonths = new List<string>();

        var horizonLimit = rule.FirstOccurrenceLocalStart.Date.AddDays(MaxHorizonDays);

        // For a date-bounded series, generate one past the cap so an over-long range is detected
        // and reported rather than quietly truncated at 200.
        var targetCount = rule.EndMode == EventRecurrenceEndMode.Count
            ? rule.OccurrenceCount!.Value
            : MaxOccurrences + 1;

        foreach (var candidate in EnumerateLocalCandidates(rule, clampedDates, skippedMonths))
        {
            if (rule.EndMode == EventRecurrenceEndMode.UntilDate
                && DateOnly.FromDateTime(candidate) > rule.EndLocalDate!.Value)
            {
                break;
            }

            if (candidate > horizonLimit)
            {
                throw new BadRequestException(
                    $"A series cannot extend more than {MaxHorizonDays / 365} years past its first occurrence.");
            }

            var resolved = ResolveToUtc(candidate, timeZone);

            occurrences.Add(new EventOccurrenceSlot(
                Index: occurrences.Count,
                LocalStart: resolved.LocalStart,
                StartUtc: resolved.StartUtc,
                EndUtc: rule.DurationMinutes.HasValue
                    ? resolved.StartUtc.AddMinutes(rule.DurationMinutes.Value)
                    : null,
                LocalStartWasInvalid: resolved.WasInvalid,
                LocalStartWasAmbiguous: resolved.WasAmbiguous));

            if (resolved.WasInvalid)
            {
                warnings.Add(
                    $"{Format(candidate)} does not exist in {rule.TimeZoneId} because clocks move forward; "
                    + $"that occurrence uses {Format(resolved.LocalStart)} instead.");
            }
            else if (resolved.WasAmbiguous)
            {
                warnings.Add(
                    $"{Format(candidate)} happens twice in {rule.TimeZoneId} because clocks move back; "
                    + "that occurrence uses the earlier of the two.");
            }

            if (occurrences.Count >= targetCount)
                break;
        }

        if (rule.EndMode == EventRecurrenceEndMode.UntilDate && occurrences.Count > MaxOccurrences)
        {
            throw new BadRequestException(
                $"That date range repeats more than {MaxOccurrences} times. "
                + "Choose a nearer end date or a longer interval.");
        }

        if (rule.EndMode == EventRecurrenceEndMode.Count && occurrences.Count < targetCount)
        {
            // Only reachable under SkipMissingMonths, where short months yield nothing.
            warnings.Add(
                $"Only {occurrences.Count} of the {targetCount} requested occurrences fall on a real date "
                + "with the current monthly rule.");
        }

        if (clampedDates.Count > 0)
        {
            warnings.Add(
                $"Day {rule.FirstOccurrenceLocalStart.Day} does not exist in every month; "
                + $"these occurrences use the last day instead: {string.Join(", ", clampedDates)}.");
        }

        if (skippedMonths.Count > 0)
        {
            warnings.Add(
                $"These months have no day {rule.FirstOccurrenceLocalStart.Day} and were skipped: "
                + $"{string.Join(", ", skippedMonths)}.");
        }

        if (occurrences.Count == 0)
            throw new BadRequestException("This repeat rule does not produce any occurrences.");

        return new EventRecurrenceExpansion(occurrences, warnings);
    }

    private static void Validate(EventRecurrenceRule rule)
    {
        if (rule.FirstOccurrenceLocalStart.Kind != DateTimeKind.Unspecified)
        {
            throw new BadRequestException(
                "The first occurrence must be a local wall-clock time, without a UTC offset.");
        }

        var maxInterval = rule.Frequency == EventRecurrenceFrequency.Monthly
            ? MaxIntervalMonthly
            : MaxIntervalDailyWeekly;

        if (rule.Interval < 1 || rule.Interval > maxInterval)
            throw new BadRequestException($"Repeat interval must be between 1 and {maxInterval}.");

        if (rule.DurationMinutes is <= 0)
            throw new BadRequestException("Event duration must be greater than zero minutes.");

        if (rule.ByWeekdays is { Count: > 0 } && rule.Frequency != EventRecurrenceFrequency.Weekly)
            throw new BadRequestException("Specific weekdays can only be chosen for a weekly series.");

        switch (rule.EndMode)
        {
            case EventRecurrenceEndMode.Count:
                if (rule.OccurrenceCount is null or < 1)
                    throw new BadRequestException("A repeat count of at least 1 is required.");

                if (rule.OccurrenceCount > MaxOccurrences)
                {
                    throw new BadRequestException(
                        $"A series cannot have more than {MaxOccurrences} occurrences.");
                }

                break;

            case EventRecurrenceEndMode.UntilDate:
                if (rule.EndLocalDate is null)
                    throw new BadRequestException("An end date is required.");

                var firstDate = DateOnly.FromDateTime(rule.FirstOccurrenceLocalStart);

                if (rule.EndLocalDate.Value < firstDate)
                    throw new BadRequestException("The end date must be on or after the first occurrence.");

                // Checked here rather than during iteration: the occurrence cap would otherwise
                // stop the loop long before a distant end date was ever examined, and the series
                // would be silently truncated instead of rejected.
                if (rule.EndLocalDate.Value.DayNumber - firstDate.DayNumber > MaxHorizonDays)
                {
                    throw new BadRequestException(
                        $"A series cannot extend more than {MaxHorizonDays / 365} years past its first occurrence.");
                }

                break;

            default:
                throw new BadRequestException("Unsupported repeat end mode.");
        }
    }

    /// <summary>
    /// Streams candidate wall-clock starts in ascending order. Never converts to UTC — that is
    /// deliberately the caller's final step, once per candidate.
    /// </summary>
    private static IEnumerable<DateTime> EnumerateLocalCandidates(
        EventRecurrenceRule rule,
        List<string> clampedDates,
        List<string> skippedMonths)
    {
        var anchor = rule.FirstOccurrenceLocalStart;
        var timeOfDay = anchor.TimeOfDay;

        switch (rule.Frequency)
        {
            case EventRecurrenceFrequency.Daily:
                for (var step = 0; step < MaxCandidateSteps; step++)
                    yield return anchor.AddDays((long)step * rule.Interval);

                break;

            case EventRecurrenceFrequency.Weekly:
                {
                    var weekdays = NormalizeWeekdays(rule.ByWeekdays, anchor.DayOfWeek);

                    // Anchor the blocks to the Monday of the first occurrence's week, so
                    // "every 2 weeks on Mon and Thu" means alternating weeks rather than
                    // "14 days after each hit".
                    var weekStart = anchor.Date.AddDays(-DaysFromMonday(anchor.DayOfWeek));
                    var emitted = 0;

                    for (var block = 0; block < MaxCandidateSteps && emitted < MaxCandidateSteps; block++)
                    {
                        var blockStart = weekStart.AddDays((long)block * 7 * rule.Interval);

                        foreach (var weekday in weekdays)
                        {
                            var candidate = blockStart.AddDays(DaysFromMonday(weekday)).Add(timeOfDay);

                            if (candidate < anchor)
                                continue;

                            emitted++;
                            yield return candidate;
                        }
                    }

                    break;
                }

            case EventRecurrenceFrequency.Monthly:
                {
                    var anchorDay = anchor.Day;

                    for (var step = 0; step < MaxCandidateSteps; step++)
                    {
                        var monthStart = new DateTime(anchor.Year, anchor.Month, 1)
                            .AddMonths(step * rule.Interval);
                        var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);

                        if (anchorDay > daysInMonth)
                        {
                            var label = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

                            if (rule.MonthlyDayPolicy == EventMonthlyDayPolicy.SkipMissingMonths)
                            {
                                if (skippedMonths.Count < 6)
                                    skippedMonths.Add(label);

                                continue;
                            }

                            if (clampedDates.Count < 6)
                                clampedDates.Add(label);
                        }

                        var day = Math.Min(anchorDay, daysInMonth);

                        yield return new DateTime(monthStart.Year, monthStart.Month, day).Add(timeOfDay);
                    }

                    break;
                }

            default:
                throw new BadRequestException("Unsupported repeat frequency.");
        }
    }

    private static IReadOnlyList<DayOfWeek> NormalizeWeekdays(
        IReadOnlyList<DayOfWeek>? requested,
        DayOfWeek fallback)
    {
        if (requested is null || requested.Count == 0)
            return [fallback];

        return requested
            .Distinct()
            .OrderBy(DaysFromMonday)
            .ToList();
    }

    private static int DaysFromMonday(DayOfWeek day) => ((int)day - (int)DayOfWeek.Monday + 7) % 7;

    /// <summary>
    /// Converts one wall-clock time to UTC, resolving both DST discontinuities explicitly rather
    /// than letting <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> pick for us.
    /// </summary>
    private static (DateTime LocalStart, DateTime StartUtc, bool WasInvalid, bool WasAmbiguous)
        ResolveToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var wasInvalid = false;
        var resolved = local;

        if (timeZone.IsInvalidTime(local))
        {
            // The wall clock skipped this time entirely. Shift by the size of the gap rather than
            // to the first valid instant, so 2:30am becomes 3:30am and not 3:00am — the minutes
            // past the hour are part of what the organizer scheduled. Measuring the offset a day
            // either side of the transition gets the delta without assuming it is one hour.
            wasInvalid = true;

            var offsetBefore = timeZone.GetUtcOffset(local.AddDays(-1));
            var offsetAfter = timeZone.GetUtcOffset(local.AddDays(1));
            var gap = offsetAfter - offsetBefore;

            if (gap > TimeSpan.Zero)
                resolved = local + gap;

            // Belt and braces: if the measured delta did not clear the gap (an exotic rule, or a
            // transition adjacent to another), walk forward until it does.
            for (var attempt = 0; attempt < 16 && timeZone.IsInvalidTime(resolved); attempt++)
                resolved = resolved.AddMinutes(15);
        }

        if (timeZone.IsAmbiguousTime(resolved))
        {
            // The wall clock passed through this time twice. Pick the earlier instant, i.e. the
            // larger UTC offset — the still-on-daylight-time reading. ConvertTimeToUtc would
            // silently choose the later, standard-time one, which is not what someone means by
            // "1:30am" on the day the clocks go back.
            var offsets = timeZone.GetAmbiguousTimeOffsets(resolved);
            var earliest = offsets.Max();

            return (resolved, DateTime.SpecifyKind(resolved - earliest, DateTimeKind.Utc), wasInvalid, true);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(resolved, DateTimeKind.Unspecified),
            timeZone);

        return (resolved, utc, wasInvalid, false);
    }

    private static string Format(DateTime local) =>
        local.ToString("d MMM yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);
}
