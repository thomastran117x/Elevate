using backend.main.shared.exceptions.http;

namespace backend.main.features.events.series;

/// <summary>
/// Resolves IANA time zone identifiers, and refuses anything else.
/// <para>
/// Since .NET 6, <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> accepts IANA identifiers on
/// every platform, mapping them through ICU on Windows. This project qualifies: nothing sets
/// <c>InvariantGlobalization</c>, and the runtime image is Debian-based <c>aspnet:9.0</c>, which
/// ships ICU and tzdata. Windows development machines and Linux CI therefore agree.
/// </para>
/// <para>
/// Two caveats worth knowing. First, on Windows the resolved zone is backed by the Windows
/// dynamic-DST table, which matches tzdata for major zones in the modern era but diverges for
/// historical rules — tests should assert against recent or future years, not pre-2010 dates.
/// Second, a small number of IANA zones have no Windows equivalent and throw only on Windows;
/// those surface here as a 400 rather than a 500.
/// </para>
/// </summary>
public static class EventSeriesTimeZones
{
    public const int MaxTimeZoneIdLength = 64;

    /// <summary>
    /// Resolves an IANA identifier such as <c>Australia/Sydney</c>.
    /// </summary>
    /// <exception cref="BadRequestException">
    /// The identifier is blank, too long, not IANA-shaped, or unknown to the runtime.
    /// </exception>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new BadRequestException("A time zone is required for a recurring series.");

        var trimmed = timeZoneId.Trim();

        if (trimmed.Length > MaxTimeZoneIdLength)
            throw new BadRequestException($"Time zone '{trimmed}' is not a valid IANA identifier.");

        // Reject Windows-style names ("Eastern Standard Time"). They would resolve on a Windows
        // developer machine and then fail on a Linux pod, so the stored value must always be the
        // portable IANA form. Every IANA id contains a '/' apart from the UTC aliases.
        if (!trimmed.Contains('/')
            && !string.Equals(trimmed, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(
                $"Time zone '{trimmed}' must be an IANA identifier such as 'Australia/Sydney'.");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(trimmed);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new BadRequestException($"Unknown time zone '{trimmed}'.");
        }
    }

    /// <summary>
    /// Fails fast at startup when the runtime cannot resolve IANA zones at all — a misconfigured
    /// base image (Alpine without <c>tzdata</c>, or <c>InvariantGlobalization</c>) would otherwise
    /// only surface on an organizer's first recurring-event request.
    /// </summary>
    public static void EnsureRuntimeSupport()
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                "Recurring events require IANA time zone data, which this runtime cannot resolve. "
                + "Ensure ICU and tzdata are installed and InvariantGlobalization is not enabled.",
                e);
        }
    }
}
