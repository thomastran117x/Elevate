using System.ComponentModel.DataAnnotations;

using backend.main.application.features;
using backend.main.features.events.series;
using backend.main.features.events.series.contracts.requests;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Series;

public class EventSeriesRequestValidationTests
{
    // ------------------------------------------------------------------ recurrence rule

    [Fact]
    public void RecurrenceRule_ShouldAcceptAWellFormedWeeklyRule()
    {
        Validate(Rule()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-03-03T19:00:00Z")]
    [InlineData("2026-03-03T19:00:00+11:00")]
    [InlineData("not a date")]
    [InlineData("")]
    public void RecurrenceRule_ShouldRejectAStartThatIsNotABareWallClock(string start)
    {
        // A trailing Z or offset means the client already committed to an instant, which
        // defeats the point of pairing a wall clock with a separate zone.
        var results = Validate(Rule(r => r.StartLocalDateTime = start));

        results.Should().Contain(r => r.ErrorMessage!.Contains("StartLocalDateTime"));
    }

    [Fact]
    public void RecurrenceRule_ShouldParseABareWallClockAsUnspecified()
    {
        Rule().TryParseLocalStart(out var parsed).Should().BeTrue();

        parsed.Kind.Should().Be(DateTimeKind.Unspecified);
        parsed.Should().Be(new DateTime(2026, 3, 3, 19, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void RecurrenceRule_ShouldRejectWeekdaysOnANonWeeklySeries()
    {
        var results = Validate(Rule(r =>
        {
            r.Frequency = EventRecurrenceFrequency.Daily;
            r.ByWeekdays = [DayOfWeek.Monday];
        }));

        results.Should().Contain(r => r.ErrorMessage!.Contains("weekly series"));
    }

    [Fact]
    public void RecurrenceRule_ShouldCapMonthlyIntervalsAtTwoYears()
    {
        var results = Validate(Rule(r =>
        {
            r.Frequency = EventRecurrenceFrequency.Monthly;
            r.Interval = 25;
        }));

        results.Should().Contain(r => r.ErrorMessage!.Contains("24 months"));
    }

    [Fact]
    public void RecurrenceRule_ShouldRequireACountWhenEndingByCount()
    {
        var results = Validate(Rule(r => r.OccurrenceCount = null));

        results.Should().Contain(r => r.ErrorMessage!.Contains("OccurrenceCount is required"));
    }

    [Fact]
    public void RecurrenceRule_ShouldRequireAParsableEndDateWhenEndingByDate()
    {
        var results = Validate(Rule(r =>
        {
            r.EndMode = EventRecurrenceEndMode.UntilDate;
            r.OccurrenceCount = null;
            r.EndLocalDate = "the end of time";
        }));

        results.Should().Contain(r => r.ErrorMessage!.Contains("EndLocalDate is required"));
    }

    [Fact]
    public void RecurrenceRule_ShouldProjectOntoTheExpanderRule()
    {
        var rule = Rule(r =>
        {
            r.EndMode = EventRecurrenceEndMode.UntilDate;
            r.OccurrenceCount = null;
            r.EndLocalDate = "2026-12-31";
        }).ToRule();

        rule.TimeZoneId.Should().Be("America/New_York");
        rule.EndLocalDate.Should().Be(new DateOnly(2026, 12, 31));
        rule.FirstOccurrenceLocalStart.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    // ------------------------------------------------------------------ extend

    [Theory]
    [InlineData(null, null)]
    [InlineData(5, "2026-12-31")]
    public void Extend_ShouldRequireExactlyOneTerminator(int? count, string? until)
    {
        var request = new ExtendEventSeriesRequest { OccurrenceCount = count, UntilLocalDate = until };

        Validate(request).Should().Contain(r => r.ErrorMessage!.Contains("exactly one"));
    }

    [Fact]
    public void Extend_ShouldRejectAnUnparsableDate()
    {
        var request = new ExtendEventSeriesRequest { UntilLocalDate = "soon" };

        Validate(request).Should().Contain(r => r.ErrorMessage!.Contains("UntilLocalDate"));
    }

    [Fact]
    public void Extend_ShouldAcceptASingleTerminator()
    {
        Validate(new ExtendEventSeriesRequest { OccurrenceCount = 8 }).Should().BeEmpty();
        Validate(new ExtendEventSeriesRequest { UntilLocalDate = "2026-12-31" }).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ update future

    [Fact]
    public void UpdateFuture_ShouldRequireAtLeastOneChange()
    {
        var results = Validate(new UpdateFutureOccurrencesRequest { FromEventId = 12 });

        results.Should().Contain(r => r.ErrorMessage!.Contains("at least one field"));
    }

    [Fact]
    public void UpdateFuture_ShouldAcceptASingleFieldChange()
    {
        Validate(new UpdateFutureOccurrencesRequest { FromEventId = 12, Location = "New Hall" })
            .Should().BeEmpty();
    }

    [Fact]
    public void UpdateFuture_ShouldRejectAnUnparsableStartTime()
    {
        var results = Validate(new UpdateFutureOccurrencesRequest
        {
            FromEventId = 12,
            LocalStartTime = "half seven"
        });

        results.Should().Contain(r => r.ErrorMessage!.Contains("LocalStartTime"));
    }

    [Fact]
    public void UpdateFuture_ShouldParseAWallClockStartTime()
    {
        var request = new UpdateFutureOccurrencesRequest { FromEventId = 12, LocalStartTime = "19:30" };

        request.TryParseLocalStartTime(out var time).Should().BeTrue();
        time.Should().Be(new TimeOnly(19, 30));
    }

    [Fact]
    public void UpdateFuture_ShouldRequireLatitudeAndLongitudeTogether()
    {
        var results = Validate(new UpdateFutureOccurrencesRequest { FromEventId = 12, Latitude = 51.5 });

        results.Should().Contain(r => r.ErrorMessage!.Contains("both be provided"));
    }

    [Fact]
    public void UpdateFuture_ShouldCapTagsAndImages()
    {
        Validate(new UpdateFutureOccurrencesRequest
        {
            FromEventId = 12,
            Tags = Enumerable.Range(0, 11).Select(i => $"tag{i}").ToList()
        }).Should().Contain(r => r.ErrorMessage!.Contains("10 tags"));

        Validate(new UpdateFutureOccurrencesRequest
        {
            FromEventId = 12,
            ImageUrls = Enumerable.Range(0, 6).Select(i => $"https://cdn.test/{i}.png").ToList()
        }).Should().Contain(r => r.ErrorMessage!.Contains("five images"));
    }

    // ------------------------------------------------------------------ disabled stub

    [Fact]
    public async Task DisabledSeriesService_ShouldRefuseEveryOperationWhenTheFlagIsOff()
    {
        var service = new DisabledEventSeriesService();

        var calls = new List<Func<Task>>
        {
            () => service.PreviewAsync(4, 7, "Organizer", new EventRecurrenceRuleRequest()),
            () => service.CreateFromDraftAsync(11, 7, "Organizer", new CreateEventSeriesRequest()),
            () => service.GetAsync(3, 7, "Organizer"),
            () => service.GetByClubAsync(4, 7, "Organizer", 1, 20),
            () => service.ExtendAsync(3, 7, "Organizer", new ExtendEventSeriesRequest()),
            () => service.PublishAsync(3, 7, "Organizer"),
            () => service.UpdateFutureOccurrencesAsync(3, 7, "Organizer", new UpdateFutureOccurrencesRequest()),
            () => service.CancelAsync(3, 7, "Organizer", new CancelEventSeriesRequest()),
            () => service.DeleteAsync(3, 7, "Organizer", new DeleteEventSeriesRequest()),
            () => service.DetachOccurrenceAsync(12, 7, "Organizer")
        };

        foreach (var call in calls)
            await call.Should().ThrowAsync<NotAvailableException>();
    }

    // ------------------------------------------------------------------ helpers

    private static EventRecurrenceRuleRequest Rule(Action<EventRecurrenceRuleRequest>? customize = null)
    {
        var request = new EventRecurrenceRuleRequest
        {
            Frequency = EventRecurrenceFrequency.Weekly,
            Interval = 1,
            StartLocalDateTime = "2026-03-03T19:00",
            DurationMinutes = 120,
            TimeZoneId = "America/New_York",
            EndMode = EventRecurrenceEndMode.Count,
            OccurrenceCount = 4
        };

        customize?.Invoke(request);

        return request;
    }

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        return results;
    }
}
