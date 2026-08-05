using backend.main.features.events.series;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Series;

/// <summary>
/// The recurrence engine's contract, with the DST cases spelled out explicitly.
/// <para>
/// Every date here is 2026 on purpose. On Windows these zones resolve through the Windows
/// dynamic-DST table rather than tzdata, and the two only agree for the modern era — asserting
/// against historical transitions would make these tests pass on Linux CI and fail on a Windows
/// developer machine.
/// </para>
/// </summary>
public class EventRecurrenceExpanderTests
{
    private const string NewYork = "America/New_York";
    private const string Sydney = "Australia/Sydney";

    private static EventRecurrenceRule Rule(
        EventRecurrenceFrequency frequency,
        DateTime firstLocalStart,
        string timeZoneId,
        int interval = 1,
        int? durationMinutes = 120,
        IReadOnlyList<DayOfWeek>? byWeekdays = null,
        EventMonthlyDayPolicy monthlyDayPolicy = EventMonthlyDayPolicy.ClampToMonthEnd,
        EventRecurrenceEndMode endMode = EventRecurrenceEndMode.Count,
        DateOnly? endLocalDate = null,
        int? occurrenceCount = 4) =>
        new(
            frequency,
            interval,
            firstLocalStart,
            durationMinutes,
            byWeekdays,
            monthlyDayPolicy,
            endMode,
            endLocalDate,
            occurrenceCount,
            timeZoneId);

    private static DateTime Local(int year, int month, int day, int hour = 19, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- frequencies

    [Fact]
    public void Expand_ShouldGenerateDailyOccurrences_HonouringInterval()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1, 9), NewYork, interval: 3, occurrenceCount: 4));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 1, 9),
            Local(2026, 6, 4, 9),
            Local(2026, 6, 7, 9),
            Local(2026, 6, 10, 9));

        result.Occurrences.Select(o => o.Index).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void Expand_ShouldGenerateWeeklyOccurrences_ForEachSelectedWeekday()
    {
        // Mondays and Thursdays, starting Monday 2026-06-01.
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Weekly,
                Local(2026, 6, 1, 18),
                NewYork,
                byWeekdays: [DayOfWeek.Monday, DayOfWeek.Thursday],
                occurrenceCount: 4));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 1, 18),
            Local(2026, 6, 4, 18),
            Local(2026, 6, 8, 18),
            Local(2026, 6, 11, 18));
    }

    [Fact]
    public void Expand_ShouldSkipSelectedWeekdaysBeforeTheFirstOccurrence()
    {
        // Starts Thursday, but Monday is also selected: the Monday of that first week
        // is in the past and must not be emitted.
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Weekly,
                Local(2026, 6, 4, 18),
                NewYork,
                byWeekdays: [DayOfWeek.Monday, DayOfWeek.Thursday],
                occurrenceCount: 3));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 4, 18),
            Local(2026, 6, 8, 18),
            Local(2026, 6, 11, 18));
    }

    [Fact]
    public void Expand_ShouldApplyWeeklyInterval_AsAlternatingWeeks()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Weekly,
                Local(2026, 6, 1, 18),
                NewYork,
                interval: 2,
                byWeekdays: [DayOfWeek.Monday, DayOfWeek.Thursday],
                occurrenceCount: 4));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 1, 18),
            Local(2026, 6, 4, 18),
            Local(2026, 6, 15, 18),
            Local(2026, 6, 18, 18));
    }

    [Fact]
    public void Expand_ShouldDefaultWeeklyToTheFirstOccurrenceWeekday_WhenNoWeekdaysGiven()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Weekly, Local(2026, 6, 3, 18), NewYork, occurrenceCount: 3));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 3, 18),
            Local(2026, 6, 10, 18),
            Local(2026, 6, 17, 18));
    }

    // ---------------------------------------------------------------- monthly edges

    [Fact]
    public void Expand_ShouldClampMonthlyDay31_ToShorterMonths()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Monthly,
                Local(2026, 1, 31, 10),
                NewYork,
                occurrenceCount: 4));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 1, 31, 10),
            Local(2026, 2, 28, 10),
            Local(2026, 3, 31, 10),
            Local(2026, 4, 30, 10));

        result.Warnings.Should().Contain(w => w.Contains("last day"));
    }

    [Fact]
    public void Expand_ShouldSkipShortMonths_WhenPolicyIsSkipMissingMonths()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Monthly,
                Local(2026, 1, 31, 10),
                NewYork,
                monthlyDayPolicy: EventMonthlyDayPolicy.SkipMissingMonths,
                occurrenceCount: 4));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 1, 31, 10),
            Local(2026, 3, 31, 10),
            Local(2026, 5, 31, 10),
            Local(2026, 7, 31, 10));

        result.Warnings.Should().Contain(w => w.Contains("skipped"));
    }

    [Fact]
    public void Expand_ShouldClampToLeapDay_InALeapYear()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Monthly,
                Local(2028, 1, 30, 10),
                NewYork,
                occurrenceCount: 2));

        result.Occurrences[1].LocalStart.Should().Be(Local(2028, 2, 29, 10));
    }

    // ---------------------------------------------------------------- DST: the point of all this

    [Fact]
    public void Expand_ShouldPreserveLocalWallClock_AcrossNorthernSpringForward()
    {
        // US clocks jump forward on 2026-03-08. A 7pm Tuesday series must stay at 7pm.
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Weekly, Local(2026, 3, 3), NewYork, occurrenceCount: 2));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 3, 3),
            Local(2026, 3, 10));

        // EST (UTC-5) before the transition, EDT (UTC-4) after.
        result.Occurrences[0].StartUtc.Should().Be(Utc(2026, 3, 4, 0));
        result.Occurrences[1].StartUtc.Should().Be(Utc(2026, 3, 10, 23));

        // The absolute gap is deliberately NOT a whole week — that is the DST hour.
        (result.Occurrences[1].StartUtc - result.Occurrences[0].StartUtc)
            .Should().Be(TimeSpan.FromDays(7) - TimeSpan.FromHours(1));
    }

    [Fact]
    public void Expand_ShouldPreserveLocalWallClock_AcrossNorthernFallBack()
    {
        // US clocks fall back on 2026-11-01.
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Weekly, Local(2026, 10, 27), NewYork, occurrenceCount: 2));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 10, 27),
            Local(2026, 11, 3));

        result.Occurrences[0].StartUtc.Should().Be(Utc(2026, 10, 27, 23));
        result.Occurrences[1].StartUtc.Should().Be(Utc(2026, 11, 4, 0));

        (result.Occurrences[1].StartUtc - result.Occurrences[0].StartUtc)
            .Should().Be(TimeSpan.FromDays(7) + TimeSpan.FromHours(1));
    }

    [Fact]
    public void Expand_ShouldPreserveLocalWallClock_AcrossSouthernHemisphereTransition()
    {
        // Sydney gains DST on 2026-10-04 — the opposite direction to the US, which catches
        // any northern-hemisphere assumption baked into the arithmetic.
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Weekly, Local(2026, 9, 27), Sydney, occurrenceCount: 2));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 9, 27),
            Local(2026, 10, 4));

        // AEST (UTC+10) then AEDT (UTC+11).
        result.Occurrences[0].StartUtc.Should().Be(Utc(2026, 9, 27, 9));
        result.Occurrences[1].StartUtc.Should().Be(Utc(2026, 10, 4, 8));
    }

    [Fact]
    public void Expand_ShouldShiftForward_WhenTheLocalTimeDoesNotExist()
    {
        // 02:30 never happens on 2026-03-08 in New York.
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 3, 7, 2, 30), NewYork, occurrenceCount: 3));

        var gapDay = result.Occurrences[1];

        gapDay.LocalStartWasInvalid.Should().BeTrue();
        gapDay.LocalStart.Should().Be(Local(2026, 3, 8, 3, 30));
        gapDay.StartUtc.Should().Be(Utc(2026, 3, 8, 7, 30));

        // Neighbours are untouched and keep the requested wall clock.
        result.Occurrences[0].LocalStart.Should().Be(Local(2026, 3, 7, 2, 30));
        result.Occurrences[0].LocalStartWasInvalid.Should().BeFalse();
        result.Occurrences[2].LocalStart.Should().Be(Local(2026, 3, 9, 2, 30));

        result.Warnings.Should().Contain(w => w.Contains("does not exist"));
    }

    [Fact]
    public void Expand_ShouldPickTheEarlierInstant_WhenTheLocalTimeHappensTwice()
    {
        // 01:30 happens twice on 2026-11-01 in New York: once on EDT (05:30Z), once on EST (06:30Z).
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 10, 31, 1, 30), NewYork, occurrenceCount: 3));

        var ambiguousDay = result.Occurrences[1];

        ambiguousDay.LocalStartWasAmbiguous.Should().BeTrue();
        ambiguousDay.LocalStart.Should().Be(Local(2026, 11, 1, 1, 30));
        ambiguousDay.StartUtc.Should().Be(Utc(2026, 11, 1, 5, 30));
        ambiguousDay.StartUtc.Should().NotBe(Utc(2026, 11, 1, 6, 30));

        result.Warnings.Should().Contain(w => w.Contains("happens twice"));
    }

    [Fact]
    public void Expand_ShouldKeepDurationAbsolute_AcrossADstTransition()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Weekly,
                Local(2026, 3, 3),
                NewYork,
                durationMinutes: 120,
                occurrenceCount: 2));

        // A two-hour event stays two real hours long on both sides of the transition, even
        // though its local end time reads differently.
        foreach (var occurrence in result.Occurrences)
            (occurrence.EndUtc!.Value - occurrence.StartUtc).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Expand_ShouldOmitEndTime_WhenDurationIsNull()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), NewYork, durationMinutes: null, occurrenceCount: 2));

        result.Occurrences.Should().OnlyContain(o => o.EndUtc == null);
    }

    [Fact]
    public void Expand_ShouldMarkEveryOccurrenceUtc()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), Sydney, occurrenceCount: 3));

        result.Occurrences.Should().OnlyContain(o => o.StartUtc.Kind == DateTimeKind.Utc);
        result.Occurrences.Should().OnlyContain(o => o.LocalStart.Kind == DateTimeKind.Unspecified);
    }

    // ---------------------------------------------------------------- terminators

    [Fact]
    public void Expand_ShouldProduceExactlyTheRequestedCount()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), NewYork, occurrenceCount: 12));

        result.Occurrences.Should().HaveCount(12);
    }

    [Fact]
    public void Expand_ShouldTreatTheEndDateAsInclusive()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 1),
                NewYork,
                endMode: EventRecurrenceEndMode.UntilDate,
                endLocalDate: new DateOnly(2026, 6, 4),
                occurrenceCount: null));

        result.Occurrences.Select(o => o.LocalStart).Should().Equal(
            Local(2026, 6, 1),
            Local(2026, 6, 2),
            Local(2026, 6, 3),
            Local(2026, 6, 4));
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void Expand_ShouldReject_WhenCountExceedsTheCap()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), NewYork, occurrenceCount: 201));

        act.Should().Throw<BadRequestException>().WithMessage("*more than 200*");
    }

    [Fact]
    public void Expand_ShouldReject_WhenTheHorizonExceedsThreeYears()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 1),
                NewYork,
                endMode: EventRecurrenceEndMode.UntilDate,
                endLocalDate: new DateOnly(2035, 6, 1),
                occurrenceCount: null));

        act.Should().Throw<BadRequestException>().WithMessage("*years*");
    }

    [Fact]
    public void Expand_ShouldReject_ADateRangeThatRepeatsPastTheCap_RatherThanTruncating()
    {
        // Daily for a year is ~365 occurrences. Silently returning the first 200 would hand the
        // organizer a series that stops months early with no indication why.
        var act = () => EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 1),
                NewYork,
                endMode: EventRecurrenceEndMode.UntilDate,
                endLocalDate: new DateOnly(2027, 5, 1),
                occurrenceCount: null));

        act.Should().Throw<BadRequestException>().WithMessage("*more than 200 times*");
    }

    [Fact]
    public void Expand_ShouldAllowADateRange_ThatLandsExactlyOnTheCap()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 1),
                NewYork,
                endMode: EventRecurrenceEndMode.UntilDate,
                endLocalDate: new DateOnly(2026, 6, 1).AddDays(EventRecurrenceExpander.MaxOccurrences - 1),
                occurrenceCount: null));

        result.Occurrences.Should().HaveCount(EventRecurrenceExpander.MaxOccurrences);
    }

    [Fact]
    public void Expand_ShouldReject_AnUnknownTimeZone()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), "Mars/Olympus_Mons"));

        act.Should().Throw<BadRequestException>().WithMessage("*Unknown time zone*");
    }

    [Fact]
    public void Expand_ShouldReject_AWindowsTimeZoneId()
    {
        // Would resolve on a Windows dev box and then fail on a Linux pod.
        var act = () => EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), "Eastern Standard Time"));

        act.Should().Throw<BadRequestException>().WithMessage("*IANA identifier*");
    }

    [Fact]
    public void Expand_ShouldAcceptUtc()
    {
        var result = EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1, 9), "UTC", occurrenceCount: 2));

        result.Occurrences[0].StartUtc.Should().Be(Utc(2026, 6, 1, 9));
    }

    [Fact]
    public void Expand_ShouldReject_AStartThatAlreadyCarriesAnOffset()
    {
        var rule = Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), NewYork) with
        {
            FirstOccurrenceLocalStart = new DateTime(2026, 6, 1, 19, 0, 0, DateTimeKind.Utc)
        };

        var act = () => EventRecurrenceExpander.Expand(rule);

        act.Should().Throw<BadRequestException>().WithMessage("*local wall-clock*");
    }

    [Fact]
    public void Expand_ShouldReject_AnOutOfRangeInterval()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Monthly, Local(2026, 6, 1), NewYork, interval: 25));

        act.Should().Throw<BadRequestException>().WithMessage("*between 1 and 24*");
    }

    [Fact]
    public void Expand_ShouldReject_WeekdaysOnANonWeeklySeries()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 1),
                NewYork,
                byWeekdays: [DayOfWeek.Monday]));

        act.Should().Throw<BadRequestException>().WithMessage("*weekly series*");
    }

    [Fact]
    public void Expand_ShouldReject_AnEndDateBeforeTheFirstOccurrence()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(
                EventRecurrenceFrequency.Daily,
                Local(2026, 6, 10),
                NewYork,
                endMode: EventRecurrenceEndMode.UntilDate,
                endLocalDate: new DateOnly(2026, 6, 1),
                occurrenceCount: null));

        act.Should().Throw<BadRequestException>().WithMessage("*on or after*");
    }

    [Fact]
    public void Expand_ShouldReject_AMissingCount()
    {
        var act = () => EventRecurrenceExpander.Expand(
            Rule(EventRecurrenceFrequency.Daily, Local(2026, 6, 1), NewYork, occurrenceCount: null));

        act.Should().Throw<BadRequestException>().WithMessage("*count of at least 1*");
    }
}
