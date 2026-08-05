using backend.main.features.events.series;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Series;

/// <summary>
/// A canary for the runtime's time zone data. If ICU or tzdata goes missing — an Alpine base
/// image, or <c>InvariantGlobalization</c> creeping into a csproj — these fail with a clear
/// message instead of the recurrence tests failing obscurely somewhere in the arithmetic.
/// </summary>
public class EventSeriesTimeZonesTests
{
    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Europe/Berlin")]
    [InlineData("Australia/Sydney")]
    [InlineData("Asia/Kolkata")]
    [InlineData("UTC")]
    public void Resolve_ShouldFindIanaZones_OnThisPlatform(string timeZoneId)
    {
        var act = () => EventSeriesTimeZones.Resolve(timeZoneId);

        act.Should().NotThrow(
            "IANA identifiers must resolve on both Windows development machines and Linux CI");
    }

    [Fact]
    public void Resolve_ShouldMatchKnownOffsets_ForFixedInstants()
    {
        // Pins the platform's DST table against tzdata for exactly the era the recurrence
        // tests assert on. Windows and tzdata agree here; they would not for, say, 2005.
        var newYork = EventSeriesTimeZones.Resolve("America/New_York");

        newYork.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(-5), "January is EST");
        newYork.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(-4), "July is EDT");

        var sydney = EventSeriesTimeZones.Resolve("Australia/Sydney");

        sydney.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(11), "January is AEDT");
        sydney.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(10), "July is AEST");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ShouldReject_ABlankIdentifier(string? timeZoneId)
    {
        var act = () => EventSeriesTimeZones.Resolve(timeZoneId);

        act.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Resolve_ShouldReject_AWindowsIdentifier()
    {
        var act = () => EventSeriesTimeZones.Resolve("Eastern Standard Time");

        act.Should().Throw<BadRequestException>().WithMessage("*IANA identifier*");
    }

    [Fact]
    public void Resolve_ShouldReject_AnUnknownZone()
    {
        var act = () => EventSeriesTimeZones.Resolve("Mars/Olympus_Mons");

        act.Should().Throw<BadRequestException>().WithMessage("*Unknown time zone*");
    }

    [Fact]
    public void EnsureRuntimeSupport_ShouldSucceed()
    {
        var act = EventSeriesTimeZones.EnsureRuntimeSupport;

        act.Should().NotThrow();
    }
}
