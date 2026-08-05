using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace backend.main.features.events.series.contracts.requests;

/// <summary>Dry-run expansion, used by the wizard's live preview.</summary>
public class PreviewEventSeriesRequest
{
    [Required]
    public EventRecurrenceRuleRequest Recurrence { get; set; } = new();
}

/// <summary>Turns an existing draft into occurrence 0 of a new series.</summary>
public class CreateEventSeriesRequest
{
    [Required]
    public EventRecurrenceRuleRequest Recurrence { get; set; } = new();
}

/// <summary>Generates further occurrences for an existing series under a revised terminator.</summary>
public class ExtendEventSeriesRequest : IValidatableObject
{
    /// <summary>New total occurrence count. Mutually exclusive with <see cref="UntilLocalDate"/>.</summary>
    [Range(1, EventRecurrenceExpander.MaxOccurrences)]
    public int? OccurrenceCount
    {
        get; set;
    }

    /// <summary>New inclusive local cutoff, as <c>yyyy-MM-dd</c>.</summary>
    public string? UntilLocalDate
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasCount = OccurrenceCount.HasValue;
        var hasDate = !string.IsNullOrWhiteSpace(UntilLocalDate);

        if (hasCount == hasDate)
        {
            yield return new ValidationResult(
                "Provide exactly one of OccurrenceCount or UntilLocalDate.",
                [nameof(OccurrenceCount), nameof(UntilLocalDate)]);
        }

        if (hasDate && !DateOnly.TryParse(
                UntilLocalDate!.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
        {
            yield return new ValidationResult(
                "UntilLocalDate must be a date such as '2026-12-31'.",
                [nameof(UntilLocalDate)]);
        }
    }
}

/// <summary>
/// Applies a patch to every future occurrence from a pivot onward.
/// <para>
/// There is deliberately no <c>StartTime</c>/<c>EndTime</c> here. Retiming a series is expressed
/// as a local wall-clock time plus the series' own zone, so a shifted series survives DST the
/// same way the original generation did; accepting a UTC instant would reintroduce the drift.
/// </para>
/// </summary>
public class UpdateFutureOccurrencesRequest : IValidatableObject
{
    /// <summary>The occurrence the organizer is editing. It and everything after it are in scope.</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int FromEventId
    {
        get; set;
    }

    /// <summary>
    /// Also rewrite occurrences that were previously edited on their own. Off by default so a
    /// deliberate one-off change is not silently undone.
    /// </summary>
    public bool IncludeOverridden { get; set; } = false;

    [StringLength(30, MinimumLength = 3)]
    public string? Name
    {
        get; set;
    }

    [StringLength(200, MinimumLength = 10)]
    public string? Description
    {
        get; set;
    }

    [StringLength(50)]
    public string? Location
    {
        get; set;
    }

    [StringLength(100)]
    public string? VenueName
    {
        get; set;
    }

    [StringLength(100)]
    public string? City
    {
        get; set;
    }

    [Range(-90, 90)]
    public double? Latitude
    {
        get; set;
    }

    [Range(-180, 180)]
    public double? Longitude
    {
        get; set;
    }

    [Range(1, 10_000)]
    public int? MaxParticipants
    {
        get; set;
    }

    [Range(0, 50_000)]
    public int? RegisterCost
    {
        get; set;
    }

    public bool? IsPrivate
    {
        get; set;
    }

    public bool? WaitlistEnabled
    {
        get; set;
    }

    /// <summary>Annotated so the category can be posted as its name, e.g. "Gaming".</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventCategory? Category
    {
        get; set;
    }

    public List<string>? Tags
    {
        get; set;
    }

    public List<string>? ImageUrls
    {
        get; set;
    }

    /// <summary>New local start time-of-day, as <c>HH:mm</c>, interpreted in the series' zone.</summary>
    public string? LocalStartTime
    {
        get; set;
    }

    [Range(1, 10080)]
    public int? DurationMinutes
    {
        get; set;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LocalStartTime is not null && !TryParseLocalStartTime(out _))
        {
            yield return new ValidationResult(
                "LocalStartTime must be a time of day such as '19:00'.",
                [nameof(LocalStartTime)]);
        }

        if (Latitude.HasValue != Longitude.HasValue)
        {
            yield return new ValidationResult(
                "Latitude and longitude must both be provided, or both omitted.",
                [nameof(Latitude), nameof(Longitude)]);
        }

        if (Tags is { Count: > 10 })
            yield return new ValidationResult("A maximum of 10 tags are allowed.", [nameof(Tags)]);

        if (ImageUrls is { Count: > 5 })
            yield return new ValidationResult("At most five images are allowed.", [nameof(ImageUrls)]);

        if (!HasAnyChange())
        {
            yield return new ValidationResult(
                "Provide at least one field to update.",
                [nameof(FromEventId)]);
        }
    }

    public bool TryParseLocalStartTime(out TimeOnly time) =>
        TimeOnly.TryParse(
            LocalStartTime?.Trim(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out time);

    private bool HasAnyChange() =>
        Name is not null
        || Description is not null
        || Location is not null
        || VenueName is not null
        || City is not null
        || Latitude.HasValue
        || Longitude.HasValue
        || MaxParticipants.HasValue
        || RegisterCost.HasValue
        || IsPrivate.HasValue
        || WaitlistEnabled.HasValue
        || Category.HasValue
        || Tags is not null
        || ImageUrls is not null
        || LocalStartTime is not null
        || DurationMinutes.HasValue;
}

/// <summary>Cancels occurrences without deleting anything.</summary>
public class CancelEventSeriesRequest
{
    /// <summary>
    /// Leave occurrences that have already started alone. On by default — retroactively
    /// cancelling an event people attended is almost never what an organizer means.
    /// </summary>
    public bool FutureOnly { get; set; } = true;
}

/// <summary>Deletes a series, choosing how much of it goes with the series row.</summary>
public class DeleteEventSeriesRequest
{
    /// <summary>Annotated so the scope can be posted as its name, e.g. "AllUnregistered".</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventSeriesDeleteScope Scope { get; set; } = EventSeriesDeleteScope.FutureDrafts;
}
