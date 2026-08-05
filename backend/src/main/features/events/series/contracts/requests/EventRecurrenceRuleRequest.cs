using System.ComponentModel.DataAnnotations;

namespace backend.main.features.events.series.contracts.requests;

/// <summary>
/// The repeat rule as it arrives over the wire.
/// <para>
/// <see cref="StartLocalDateTime"/> is a <em>string</em> on purpose. Binding it as a DateTime
/// would let System.Text.Json attach a UTC or local <c>Kind</c> depending on whether the client
/// happened to send a <c>Z</c> suffix, quietly reinterpreting the organizer's wall-clock choice
/// against the wrong zone. The zone is carried separately in <see cref="TimeZoneId"/>, and the
/// two are combined only by <see cref="EventRecurrenceExpander"/>.
/// </para>
/// </summary>
public class EventRecurrenceRuleRequest : IValidatableObject
{
    [Required]
    public EventRecurrenceFrequency Frequency
    {
        get; set;
    }

    [Range(1, 52)]
    public int Interval { get; set; } = 1;

    /// <summary>Weekly only. Empty means "the same weekday as the first occurrence".</summary>
    public List<DayOfWeek>? ByWeekdays
    {
        get; set;
    }

    public EventMonthlyDayPolicy MonthlyDayPolicy { get; set; } = EventMonthlyDayPolicy.ClampToMonthEnd;

    /// <summary>Local wall-clock start, as <c>yyyy-MM-ddTHH:mm[:ss]</c> with no offset.</summary>
    [Required]
    [StringLength(19, MinimumLength = 16)]
    public string StartLocalDateTime { get; set; } = string.Empty;

    /// <summary>Event length in minutes. Null means the occurrences have no end time.</summary>
    [Range(1, 10080)]
    public int? DurationMinutes
    {
        get; set;
    }

    [Required]
    [StringLength(EventSeriesTimeZones.MaxTimeZoneIdLength)]
    public string TimeZoneId { get; set; } = string.Empty;

    [Required]
    public EventRecurrenceEndMode EndMode
    {
        get; set;
    }

    /// <summary>Inclusive local cutoff, as <c>yyyy-MM-dd</c>. Required when EndMode is UntilDate.</summary>
    public string? EndLocalDate
    {
        get; set;
    }

    /// <summary>Required when EndMode is Count.</summary>
    [Range(1, EventRecurrenceExpander.MaxOccurrences)]
    public int? OccurrenceCount
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TryParseLocalStart(out _))
        {
            yield return new ValidationResult(
                "StartLocalDateTime must be a local date and time such as '2026-03-03T19:00', with no time zone offset.",
                [nameof(StartLocalDateTime)]);
        }

        if (ByWeekdays is { Count: > 0 } && Frequency != EventRecurrenceFrequency.Weekly)
        {
            yield return new ValidationResult(
                "ByWeekdays can only be used with a weekly series.",
                [nameof(ByWeekdays)]);
        }

        if (Frequency == EventRecurrenceFrequency.Monthly && Interval > 24)
        {
            yield return new ValidationResult(
                "A monthly series can repeat at most every 24 months.",
                [nameof(Interval)]);
        }

        switch (EndMode)
        {
            case EventRecurrenceEndMode.Count when OccurrenceCount is null:
                yield return new ValidationResult(
                    "OccurrenceCount is required when EndMode is Count.",
                    [nameof(OccurrenceCount)]);

                break;

            case EventRecurrenceEndMode.UntilDate when !TryParseEndDate(out _):
                yield return new ValidationResult(
                    "EndLocalDate is required when EndMode is UntilDate, as 'yyyy-MM-dd'.",
                    [nameof(EndLocalDate)]);

                break;
        }
    }

    /// <summary>
    /// Parses the wall-clock start, guaranteeing <see cref="DateTimeKind.Unspecified"/>. Any
    /// trailing offset or <c>Z</c> is rejected rather than silently converted.
    /// </summary>
    public bool TryParseLocalStart(out DateTime localStart)
    {
        localStart = default;

        var value = StartLocalDateTime?.Trim();

        if (string.IsNullOrEmpty(value)
            || value.EndsWith('Z')
            || value.Contains('+')
            || value.LastIndexOf('-') > value.IndexOf('T'))
        {
            return false;
        }

        if (!DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        localStart = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return true;
    }

    public bool TryParseEndDate(out DateOnly endDate) =>
        DateOnly.TryParse(
            EndLocalDate?.Trim(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out endDate);

    /// <summary>Projects the validated request onto the expander's rule type.</summary>
    public EventRecurrenceRule ToRule()
    {
        TryParseLocalStart(out var localStart);

        DateOnly? endDate = TryParseEndDate(out var parsedEnd) ? parsedEnd : null;

        return new EventRecurrenceRule(
            Frequency,
            Interval,
            localStart,
            DurationMinutes,
            ByWeekdays,
            MonthlyDayPolicy,
            EndMode,
            endDate,
            OccurrenceCount,
            TimeZoneId?.Trim() ?? string.Empty);
    }
}
