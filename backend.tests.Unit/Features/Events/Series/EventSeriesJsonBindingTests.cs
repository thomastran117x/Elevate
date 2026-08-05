using System.Text.Json;

using backend.main.features.events;
using backend.main.features.events.series;
using backend.main.features.events.series.contracts.requests;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Series;

/// <summary>
/// The recurrence contracts are posted by the Angular editor, which sends enums as their string
/// names. There is no global <c>JsonStringEnumConverter</c> in this application, so each enum
/// property has to opt in explicitly or model binding rejects the request before the service
/// ever runs. These tests bind through the same default options ASP.NET Core uses.
/// </summary>
public class EventSeriesJsonBindingTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RecurrenceRule_ShouldBindEnumsSentAsStrings()
    {
        const string json = """
        {
          "frequency": "Weekly",
          "interval": 2,
          "byWeekdays": ["Tuesday", "Thursday"],
          "monthlyDayPolicy": "ClampToMonthEnd",
          "startLocalDateTime": "2026-03-03T19:00",
          "durationMinutes": 120,
          "timeZoneId": "America/New_York",
          "endMode": "Count",
          "occurrenceCount": 4
        }
        """;

        var request = JsonSerializer.Deserialize<EventRecurrenceRuleRequest>(json, WebDefaults)!;

        request.Frequency.Should().Be(EventRecurrenceFrequency.Weekly);
        request.MonthlyDayPolicy.Should().Be(EventMonthlyDayPolicy.ClampToMonthEnd);
        request.EndMode.Should().Be(EventRecurrenceEndMode.Count);
        request.ByWeekdays.Should().Equal(DayOfWeek.Tuesday, DayOfWeek.Thursday);
    }

    [Fact]
    public void RecurrenceRule_ShouldStillBindEnumsSentAsNumbers()
    {
        const string json = """
        {
          "frequency": 1,
          "interval": 1,
          "monthlyDayPolicy": 0,
          "startLocalDateTime": "2026-03-03T19:00",
          "timeZoneId": "UTC",
          "endMode": 0,
          "occurrenceCount": 2
        }
        """;

        var request = JsonSerializer.Deserialize<EventRecurrenceRuleRequest>(json, WebDefaults)!;

        request.Frequency.Should().Be(EventRecurrenceFrequency.Weekly);
        request.EndMode.Should().Be(EventRecurrenceEndMode.Count);
    }

    [Fact]
    public void UpdateFutureOccurrences_ShouldBindCategorySentAsAString()
    {
        const string json = """
        { "fromEventId": 12, "category": "Gaming" }
        """;

        var request = JsonSerializer.Deserialize<UpdateFutureOccurrencesRequest>(json, WebDefaults)!;

        request.Category.Should().Be(EventCategory.Gaming);
    }

    [Fact]
    public void DeleteSeries_ShouldBindScopeSentAsAString()
    {
        const string json = """
        { "scope": "AllUnregistered" }
        """;

        var request = JsonSerializer.Deserialize<DeleteEventSeriesRequest>(json, WebDefaults)!;

        request.Scope.Should().Be(EventSeriesDeleteScope.AllUnregistered);
    }

    [Fact]
    public void CreateSeries_ShouldBindANestedRuleSentWithStringEnums()
    {
        const string json = """
        {
          "recurrence": {
            "frequency": "Monthly",
            "interval": 1,
            "monthlyDayPolicy": "SkipMissingMonths",
            "startLocalDateTime": "2026-01-31T10:00",
            "timeZoneId": "Australia/Sydney",
            "endMode": "UntilDate",
            "endLocalDate": "2026-12-31"
          }
        }
        """;

        var request = JsonSerializer.Deserialize<CreateEventSeriesRequest>(json, WebDefaults)!;

        request.Recurrence.Frequency.Should().Be(EventRecurrenceFrequency.Monthly);
        request.Recurrence.MonthlyDayPolicy.Should().Be(EventMonthlyDayPolicy.SkipMissingMonths);
        request.Recurrence.EndMode.Should().Be(EventRecurrenceEndMode.UntilDate);
    }
}
