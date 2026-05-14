using System.Globalization;

namespace backend.main.seeders;

public static class ThematicEventFactory
{
    public static IReadOnlyList<SeedEventDefinition> BuildEvents(
        SeedClubDefinition club,
        DateTime seasonStartUtc)
    {
        var publicEvents = ExpandSeries(club, club.PublicSeries, seasonStartUtc);
        var privateEvents = ExpandSeries(club, club.PrivateSeries, seasonStartUtc);

        if (publicEvents.Count != 50)
            throw new InvalidOperationException(
                $"Club '{club.Name}' must expand to exactly 50 public events, but expanded to {publicEvents.Count}.");

        if (privateEvents.Count != 5)
            throw new InvalidOperationException(
                $"Club '{club.Name}' must expand to exactly 5 private events, but expanded to {privateEvents.Count}.");

        return publicEvents
            .Concat(privateEvents)
            .OrderBy(ev => ev.StartTimeUtc)
            .ThenBy(ev => ev.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SeedEventDefinition> ExpandSeries(
        SeedClubDefinition club,
        IReadOnlyList<SeedEventSeriesDefinition> seriesDefinitions,
        DateTime seasonStartUtc)
    {
        var events = new List<SeedEventDefinition>();

        foreach (var series in seriesDefinitions)
        {
            for (var occurrence = 1; occurrence <= series.OccurrenceCount; occurrence++)
            {
                var venue = series.Venues[(occurrence - 1) % series.Venues.Count];
                var eventStart = seasonStartUtc.Date
                    .AddDays(series.StartDayOffset + ((occurrence - 1) * series.CadenceDays))
                    .AddHours(series.StartHourUtc);
                var eventEnd = eventStart.AddHours(series.DurationHours);
                var maxParticipants = series.BaseMaxParticipants + (((occurrence - 1) % 3) * series.CapacityStep);
                var registerCost = series.BaseRegisterCost + (((occurrence - 1) % 2) * series.CostStep);
                var createdAt = eventStart.AddDays(-21);
                var updatedAt = eventStart.AddDays(-7);
                var eventNumber = occurrence.ToString("D2", CultureInfo.InvariantCulture);

                events.Add(new SeedEventDefinition(
                    Name: $"{series.NamePrefix} {eventNumber}",
                    Description: ApplyTemplate(series.DescriptionTemplate, club, venue, eventNumber),
                    Location: venue.Location,
                    IsPrivate: series.IsPrivate,
                    MaxParticipants: maxParticipants,
                    RegisterCost: registerCost,
                    StartTimeUtc: eventStart,
                    EndTimeUtc: eventEnd,
                    Category: series.Category,
                    VenueName: venue.Name,
                    City: club.City,
                    Latitude: venue.Latitude,
                    Longitude: venue.Longitude,
                    Tags: BuildTags(club, series),
                    CreatedAtUtc: createdAt,
                    UpdatedAtUtc: updatedAt));
            }
        }

        return events;
    }

    private static string ApplyTemplate(
        string template,
        SeedClubDefinition club,
        SeedVenueDefinition venue,
        string eventNumber)
    {
        return template
            .Replace("{club}", club.Name, StringComparison.Ordinal)
            .Replace("{theme}", club.Theme, StringComparison.Ordinal)
            .Replace("{tone}", club.Tone, StringComparison.Ordinal)
            .Replace("{venue}", venue.Name, StringComparison.Ordinal)
            .Replace("{number}", eventNumber, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildTags(
        SeedClubDefinition club,
        SeedEventSeriesDefinition series)
    {
        var tags = club.ThemeTags
            .Concat(series.Tags)
            .Concat(
            [
                SeedCatalogConstants.SeedEventTag,
                SeedCatalogConstants.ClubSeedTag(club.Slug)
            ])
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return tags;
    }
}
