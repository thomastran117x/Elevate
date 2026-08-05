using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.search;
using backend.main.features.events.series;
using backend.main.features.events.series.contracts.requests;
using backend.main.features.events.versions;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events.Series;

public class EventSeriesServiceTests
{
    private const string Sydney = "Australia/Sydney";
    private const string NewYork = "America/New_York";

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task CreateFromDraftAsync_ShouldMaterializeOccurrencesAsDrafts_ReusingTheTemplate()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        var series = await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 4));

        series.Occurrences.Should().HaveCount(4);
        series.Occurrences.Should().OnlyContain(o => o.LifecycleState == EventLifecycleState.Draft);
        series.GeneratedCount.Should().Be(4);

        // The template becomes occurrence 0 rather than being duplicated, so starting a series
        // leaves no orphaned draft behind.
        series.TemplateEventId.Should().Be(template.Id);
        series.Occurrences[0].Id.Should().Be(template.Id);
        series.Occurrences.Select(o => o.OccurrenceIndex).Should().Equal(0, 1, 2, 3);
        harness.Db.Events.Count().Should().Be(4);
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldStampTheSeriesTimeZoneOnEveryOccurrence()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        var series = await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(timeZoneId: Sydney, occurrenceCount: 3));

        series.Occurrences.Should().OnlyContain(o => o.TimeZoneId == Sydney);
        series.Rule.TimeZoneId.Should().Be(Sydney);
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldCopyTemplateImagesToEveryOccurrence()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync(imageUrls: ["https://cdn.test/a.png"]);

        await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 3));

        // Occurrence 0 already has the template's image; the other two get copies of the
        // same URL, so no blob is duplicated in storage.
        harness.ImageRepositoryMock.Verify(
            repository => repository.AddImagesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldRecordAVersionAndStageSearchSyncPerOccurrence()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 3));

        harness.Db.EventVersions
            .Count(v => v.ActionType == EventVersionActions.SeriesCreate)
            .Should().Be(3);

        harness.OutboxWriterMock.Verify(
            writer => writer.StageSync(It.IsAny<EventEntity>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldThrowForbidden_ForSomeoneWhoCannotManageTheClub()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        var act = () => harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.ViewerUserId,
            harness.ViewerRole,
            Request());

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldThrowConflict_WhenTheTemplateIsAlreadyInASeries()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 2));

        var act = () => harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 2));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateFromDraftAsync_ShouldThrowConflict_WhenTheTemplateIsNotADraft()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync(lifecycleState: EventLifecycleState.Published);

        var act = () => harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request());

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ------------------------------------------------------------------ time zones

    [Fact]
    public async Task CreateFromDraftAsync_ShouldPersistUtcInstants_ThatHoldTheLocalWallClockAcrossDst()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync();

        // Weekly 7pm from 2026-03-03 in New York, spanning the 2026-03-08 spring-forward.
        var series = await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(
                timeZoneId: NewYork,
                startLocal: "2026-03-03T19:00",
                frequency: EventRecurrenceFrequency.Weekly,
                occurrenceCount: 2));

        series.Occurrences[0].StartTime.Should().Be(new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc));
        series.Occurrences[1].StartTime.Should().Be(new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------ publish

    [Fact]
    public async Task PublishAsync_ShouldPublishReadyOccurrences_AndReportTheRestAsSkipped()
    {
        await using var harness = await Harness.CreateAsync();
        var template = await harness.AddDraftAsync(imageUrls: ["https://cdn.test/a.png"]);

        var series = await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(occurrenceCount: 3));

        // Strip one occurrence's images so it fails the publish checks.
        var victim = await harness.Db.Events.Include(e => e.Images)
            .FirstAsync(e => e.OccurrenceIndex == 2);
        harness.Db.EventImages.RemoveRange(victim.Images);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.PublishAsync(series.Id, harness.OwnerUserId, harness.OwnerRole);

        result.AffectedCount.Should().Be(2);
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("not-publish-ready");

        // One unpublishable occurrence must not block the others.
        harness.Db.Events.Count(e => e.LifecycleState == EventLifecycleState.Published).Should().Be(2);
    }

    // ------------------------------------------------------------------ update all future

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldUpdateFromThePivotOnward_LeavingEarlierOnesAlone()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 4);

        var pivot = await harness.OccurrenceAsync(series.Id, index: 2);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, Location = "New Hall" });

        result.AffectedCount.Should().Be(2);

        var all = await harness.Db.Events.OrderBy(e => e.OccurrenceIndex).ToListAsync();
        all[0].Location.Should().NotBe("New Hall");
        all[1].Location.Should().NotBe("New Hall");
        all[2].Location.Should().Be("New Hall");
        all[3].Location.Should().Be("New Hall");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldNotTouchOccurrencesThatHaveAlreadyStarted()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        // Occurrences fall on 1, 8 and 15 June 2026. Move the clock to the 10th so the first
        // two have already happened and only the last is still ahead.
        harness.Clock.Advance(TimeSpan.FromDays(40));

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, Location = "New Hall" });

        result.AffectedCount.Should().Be(1);

        var all = await harness.Db.Events.OrderBy(e => e.OccurrenceIndex).ToListAsync();
        all[0].Location.Should().NotBe("New Hall");
        all[1].Location.Should().NotBe("New Hall");
        all[2].Location.Should().Be("New Hall");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldSkipIndividuallyEditedOccurrences_ByDefault()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var overridden = await harness.OccurrenceAsync(series.Id, index: 2);
        overridden.SeriesOverridden = true;
        await harness.Db.SaveChangesAsync();

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, Location = "New Hall" });

        result.AffectedCount.Should().Be(2);
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be("individually-edited");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldIncludeEditedOccurrences_WhenAskedTo()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var overridden = await harness.OccurrenceAsync(series.Id, index: 2);
        overridden.SeriesOverridden = true;
        await harness.Db.SaveChangesAsync();

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest
            {
                FromEventId = pivot.Id,
                Location = "New Hall",
                IncludeOverridden = true
            });

        result.AffectedCount.Should().Be(3);
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldSkipCapacityBelowRegistrations_ButStillUpdateTheOthers()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var busy = await harness.OccurrenceAsync(series.Id, index: 1);
        busy.RegistrationCount = 20;
        await harness.Db.SaveChangesAsync();

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, MaxParticipants = 5 });

        result.AffectedCount.Should().Be(2);
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("capacity-below-registrations");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldSkipRepricing_WhenPeopleHaveAlreadyRegistered()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);

        var busy = await harness.OccurrenceAsync(series.Id, index: 1);
        busy.RegistrationCount = 3;
        await harness.Db.SaveChangesAsync();

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, RegisterCost = 25 });

        result.AffectedCount.Should().Be(1);
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("repricing-with-registrations");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldRetimeInTheSeriesTimeZone_PreservingTheWallClock()
    {
        // Clock sits before the March occurrences, so both are in scope for the retime.
        await using var harness = await Harness.CreateAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var template = await harness.AddDraftAsync();
        var series = await harness.Service.CreateFromDraftAsync(
            template.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            Request(
                timeZoneId: NewYork,
                startLocal: "2026-03-03T19:00",
                frequency: EventRecurrenceFrequency.Weekly,
                occurrenceCount: 2));

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, LocalStartTime = "18:00" });

        var all = await harness.Db.Events.OrderBy(e => e.OccurrenceIndex).ToListAsync();

        // Both now read 6pm locally — which is a different UTC instant on each side of the
        // 2026-03-08 transition. A naive UTC shift would have produced the same offset twice.
        all[0].StartTime.Should().Be(new DateTime(2026, 3, 3, 23, 0, 0, DateTimeKind.Utc));
        all[1].StartTime.Should().Be(new DateTime(2026, 3, 10, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldPreserveTheExistingDuration_WhenRetimingWithoutANewOne()
    {
        await using var harness = await Harness.CreateAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // The template runs for two hours, and the rule carries that through to every occurrence.
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);
        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        (pivot.EndTime!.Value - pivot.StartTime!.Value).Should().Be(TimeSpan.FromHours(2));

        await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, LocalStartTime = "18:00" });

        var all = await harness.Db.Events.OrderBy(e => e.OccurrenceIndex).ToListAsync();

        // Moving the start must not quietly turn a two-hour event into a one-hour one.
        all.Should().OnlyContain(e => e.EndTime!.Value - e.StartTime!.Value == TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldApplyAnExplicitDuration_WhenOneIsGiven()
    {
        await using var harness = await Harness.CreateAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);
        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, DurationMinutes = 45 });

        var all = await harness.Db.Events.OrderBy(e => e.OccurrenceIndex).ToListAsync();

        all.Should().OnlyContain(e => e.EndTime!.Value - e.StartTime!.Value == TimeSpan.FromMinutes(45));
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldThrowNotFound_WhenThePivotIsNotInTheSeries()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);
        var stranger = await harness.AddDraftAsync();

        var act = () => harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = stranger.Id, Location = "Elsewhere" });

        await act.Should().ThrowAsync<ResourceNotFoundException>();
    }

    // ------------------------------------------------------------------ cancel

    [Fact]
    public async Task CancelAsync_ShouldCancelPublishedOccurrences_WithoutDeletingAnything()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3, publish: true);

        var result = await harness.Service.CancelAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new CancelEventSeriesRequest());

        result.AffectedCount.Should().Be(3);
        harness.Db.Events.Count().Should().Be(3, "cancelling never deletes");
        harness.Db.Events.Should().OnlyContain(e => e.LifecycleState == EventLifecycleState.Cancelled);
        harness.Db.EventSeries.Single().Status.Should().Be(EventSeriesStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAsync_ShouldReportDraftsAsSkipped_SinceTheyCannotTransitionToCancelled()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);

        var result = await harness.Service.CancelAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new CancelEventSeriesRequest());

        result.AffectedCount.Should().Be(0);
        result.Skipped.Should().HaveCount(2);
        result.Skipped.Should().OnlyContain(s => s.Reason == "draft-not-cancellable");
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task DeleteAsync_ShouldDetachOccurrencesWithRegistrations_RatherThanDeletingThem()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var booked = await harness.OccurrenceAsync(series.Id, index: 1);
        booked.RegistrationCount = 4;
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.DeleteAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new DeleteEventSeriesRequest { Scope = EventSeriesDeleteScope.AllUnregistered });

        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be("has-registrations");

        var survivor = await harness.Db.Events.SingleAsync();
        survivor.Id.Should().Be(booked.Id);
        survivor.SeriesId.Should().BeNull("a detached occurrence is an ordinary event");
        harness.Db.EventSeries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDetachEverything_WhenScopeIsSeriesRecordOnly()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        await harness.Service.DeleteAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new DeleteEventSeriesRequest { Scope = EventSeriesDeleteScope.SeriesRecordOnly });

        harness.Db.Events.Count().Should().Be(3);
        harness.Db.Events.Should().OnlyContain(e => e.SeriesId == null);
        harness.Db.EventSeries.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ detach

    [Fact]
    public async Task DetachOccurrenceAsync_ShouldClearMembership_AndRecordADetachVersion()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);
        var occurrence = await harness.OccurrenceAsync(series.Id, index: 1);

        var detached = await harness.Service.DetachOccurrenceAsync(
            occurrence.Id,
            harness.OwnerUserId,
            harness.OwnerRole);

        detached.SeriesId.Should().BeNull();
        detached.OccurrenceIndex.Should().BeNull();

        harness.Db.EventVersions
            .Count(v => v.EventId == occurrence.Id && v.ActionType == EventVersionActions.SeriesDetach)
            .Should().Be(1);

        // The rest of the series is untouched.
        harness.Db.Events.Count(e => e.SeriesId == series.Id).Should().Be(2);
    }

    [Fact]
    public async Task DetachOccurrenceAsync_ShouldExcludeTheEventFromLaterSeriesUpdates()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var detachTarget = await harness.OccurrenceAsync(series.Id, index: 2);
        await harness.Service.DetachOccurrenceAsync(detachTarget.Id, harness.OwnerUserId, harness.OwnerRole);

        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, Location = "New Hall" });

        result.AffectedCount.Should().Be(2);

        var detached = await harness.Db.Events.FirstAsync(e => e.Id == detachTarget.Id);
        detached.Location.Should().NotBe("New Hall");
    }

    [Fact]
    public async Task DetachOccurrenceAsync_ShouldThrowConflict_ForAStandaloneEvent()
    {
        await using var harness = await Harness.CreateAsync();
        var standalone = await harness.AddDraftAsync();

        var act = () => harness.Service.DetachOccurrenceAsync(
            standalone.Id,
            harness.OwnerUserId,
            harness.OwnerRole);

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ------------------------------------------------------------------ extend

    [Fact]
    public async Task ExtendAsync_ShouldOnlyAddOccurrencesBeyondTheHighWaterMark()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        var originalIds = await harness.Db.Events.Select(e => e.Id).OrderBy(id => id).ToListAsync();

        var extended = await harness.Service.ExtendAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new ExtendEventSeriesRequest { OccurrenceCount = 5 });

        extended.Occurrences.Should().HaveCount(5);
        extended.GeneratedCount.Should().Be(5);

        // Existing rows are reused, not recreated.
        var afterIds = await harness.Db.Events.Select(e => e.Id).OrderBy(id => id).ToListAsync();
        afterIds.Should().Contain(originalIds);
    }

    [Fact]
    public async Task ExtendAsync_ShouldNotCloneAnIndividuallyEditedOccurrence()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);

        // Occurrence 0 gets a one-off change and is marked as an exception.
        var edited = await harness.OccurrenceAsync(series.Id, index: 0);
        edited.Name = "One-off special";
        edited.SeriesOverridden = true;
        await harness.Db.SaveChangesAsync();

        await harness.Service.ExtendAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new ExtendEventSeriesRequest { OccurrenceCount = 5 });

        var generated = await harness.Db.Events
            .Where(e => e.OccurrenceIndex >= 3)
            .ToListAsync();

        generated.Should().HaveCount(2);

        // New dates must inherit the series' ordinary details, not the one-off edit.
        generated.Should().OnlyContain(e => e.Name != "One-off special");
    }

    [Fact]
    public async Task ExtendAsync_ShouldFallBackToTheFirstOccurrence_WhenEveryOneHasBeenEdited()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);

        foreach (var occurrence in await harness.Db.Events.ToListAsync())
            occurrence.SeriesOverridden = true;

        await harness.Db.SaveChangesAsync();

        var extended = await harness.Service.ExtendAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new ExtendEventSeriesRequest { OccurrenceCount = 3 });

        // Nothing cleaner to copy from, but extending must still work rather than fail.
        extended.Occurrences.Should().HaveCount(3);
    }

    // ------------------------------------------------------------------ override flag lifecycle

    [Fact]
    public void ApplySnapshot_ShouldRestoreTheOverrideFlag_SoARolledBackEditRejoinsTheSeries()
    {
        // Content before the one-off edit: part of the series, not overridden.
        var before = EventVersionRecorder.BuildSnapshot(new EventEntity
        {
            Name = "Weekly Tabletop",
            ClubId = 4,
            SeriesId = 3,
            OccurrenceIndex = 1,
            SeriesOverridden = false,
            Tags = []
        });

        // The occurrence has since been edited on its own and excluded from series updates.
        var edited = new EventEntity
        {
            Name = "One-off special",
            ClubId = 4,
            SeriesId = 3,
            OccurrenceIndex = 1,
            SeriesOverridden = true,
            Tags = []
        };

        EventVersionRecorder.ApplySnapshot(edited, before);

        edited.Name.Should().Be("Weekly Tabletop");
        edited.SeriesOverridden.Should().BeFalse(
            "undoing the edit should put the occurrence back in scope for series-wide updates");
    }

    [Fact]
    public void ApplySnapshot_ShouldStillNotRestoreSeriesMembership()
    {
        var snapshot = EventVersionRecorder.BuildSnapshot(new EventEntity
        {
            ClubId = 4,
            SeriesId = 3,
            OccurrenceIndex = 1,
            Tags = []
        });

        // The organizer has since detached this occurrence on purpose.
        var detached = new EventEntity { ClubId = 4, SeriesId = null, OccurrenceIndex = null, Tags = [] };

        EventVersionRecorder.ApplySnapshot(detached, snapshot);

        detached.SeriesId.Should().BeNull("a rollback must not silently re-attach a detached event");
        detached.OccurrenceIndex.Should().BeNull();
    }

    // ------------------------------------------------------------------ image validation

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldRejectImagesTheOrganizerDidNotUpload()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 3);
        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        // A blob URL this service owns but that has no upload intent for this user — the shape
        // of another organizer's image being pasted in.
        var act = () => harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest
            {
                FromEventId = pivot.Id,
                ImageUrls = ["https://blob.test/events/someone-elses.png"]
            });

        await act.Should().ThrowAsync<BadRequestException>();

        // The whole request is refused, so no occurrence is left half-updated.
        harness.Db.Events.Should().OnlyContain(e => e.Name == "Weekly Tabletop Night");
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldAcceptAnImageTheOrganizerJustUploaded()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);
        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var url = harness.RegisterUploadIntent("https://blob.test/events/mine.png");

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest { FromEventId = pivot.Id, ImageUrls = [url] });

        result.AffectedCount.Should().Be(2);
    }

    [Fact]
    public async Task UpdateFutureOccurrencesAsync_ShouldAllowResubmittingImagesTheOccurrencesAlreadyHold()
    {
        await using var harness = await Harness.CreateAsync();

        // The template's image is already attached, so its upload intent has long since expired.
        // Re-sending it is not a new upload and must not be rejected.
        var series = await harness.CreateSeriesAsync(occurrenceCount: 2);
        var pivot = await harness.OccurrenceAsync(series.Id, index: 0);

        var result = await harness.Service.UpdateFutureOccurrencesAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new UpdateFutureOccurrencesRequest
            {
                FromEventId = pivot.Id,
                ImageUrls = ["https://cdn.test/default.png"]
            });

        result.AffectedCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtendAsync_ShouldReject_WhenItWouldNotAddAnything()
    {
        await using var harness = await Harness.CreateAsync();
        var series = await harness.CreateSeriesAsync(occurrenceCount: 4);

        var act = () => harness.Service.ExtendAsync(
            series.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new ExtendEventSeriesRequest { OccurrenceCount = 2 });

        await act.Should().ThrowAsync<BadRequestException>();
    }

    // ------------------------------------------------------------------ preview

    [Fact]
    public async Task PreviewAsync_ShouldExpandWithoutPersistingAnything()
    {
        await using var harness = await Harness.CreateAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            RuleRequest(occurrenceCount: 5));

        preview.OccurrenceCount.Should().Be(5);
        preview.Occurrences.Should().HaveCount(5);
        harness.Db.Events.Should().BeEmpty();
        harness.Db.EventSeries.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_ShouldReportTheShiftingUtcOffset_AcrossADstBoundary()
    {
        await using var harness = await Harness.CreateAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            RuleRequest(
                timeZoneId: NewYork,
                startLocal: "2026-03-03T19:00",
                frequency: EventRecurrenceFrequency.Weekly,
                occurrenceCount: 2));

        preview.Occurrences[0].UtcOffset.Should().Be("-05:00");
        preview.Occurrences[1].UtcOffset.Should().Be("-04:00");
        preview.Occurrences.Should().OnlyContain(o => o.LocalStart.EndsWith("19:00:00"));
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrowForbidden_ForSomeoneWhoCannotManageTheClub()
    {
        await using var harness = await Harness.CreateAsync();

        var act = () => harness.Service.PreviewAsync(
            harness.ClubId,
            harness.ViewerUserId,
            harness.ViewerRole,
            RuleRequest());

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------ fixtures

    private static EventRecurrenceRuleRequest RuleRequest(
        string timeZoneId = Sydney,
        string startLocal = "2026-06-01T19:00",
        EventRecurrenceFrequency frequency = EventRecurrenceFrequency.Weekly,
        int? occurrenceCount = 4) => new()
        {
            Frequency = frequency,
            Interval = 1,
            StartLocalDateTime = startLocal,
            DurationMinutes = 120,
            TimeZoneId = timeZoneId,
            EndMode = EventRecurrenceEndMode.Count,
            OccurrenceCount = occurrenceCount
        };

    private static CreateEventSeriesRequest Request(
        string timeZoneId = Sydney,
        string startLocal = "2026-06-01T19:00",
        EventRecurrenceFrequency frequency = EventRecurrenceFrequency.Weekly,
        int? occurrenceCount = 4) => new()
        {
            Recurrence = RuleRequest(timeZoneId, startLocal, frequency, occurrenceCount)
        };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        /// <summary>Upload intents keyed exactly as the presigned-URL flow stores them.</summary>
        private readonly Dictionary<string, string?> _uploadIntents = [];

        public AppDatabaseContext Db
        {
            get;
        }
        public EventSeriesService Service
        {
            get;
        }
        public FakeClock Clock
        {
            get;
        }
        public Mock<IEventSeriesRepository> SeriesRepositoryMock { get; } = new();
        public Mock<IEventsRepository> EventsRepositoryMock { get; } = new();
        public Mock<IEventImageRepository> ImageRepositoryMock { get; } = new();
        public Mock<IClubService> ClubServiceMock { get; } = new();
        public Mock<IAzureBlobService> BlobServiceMock { get; } = new();
        public Mock<ICacheService> CacheMock { get; } = new();
        public Mock<IRefreshAheadCache> RefreshCacheMock { get; } = new();
        public Mock<IEventSearchOutboxWriter> OutboxWriterMock { get; } = new();

        public int ClubId => 4;
        public int OwnerUserId => 7;
        public int ViewerUserId => 99;
        public string OwnerRole => "Organizer";
        public string ViewerRole => "Participant";

        private Harness(SqliteConnection connection, AppDatabaseContext db, DateTime utcNow)
        {
            _connection = connection;
            Db = db;
            Clock = new FakeClock(utcNow);

            ClubServiceMock
                .Setup(service => service.GetClub(It.IsAny<int>()))
                .ReturnsAsync(new Club
                {
                    Id = ClubId,
                    Name = "Board Games Club",
                    Description = "A club for tabletop events.",
                    Clubtype = ClubType.Gaming,
                    ClubImage = "https://cdn.test/clubs/gaming.png"
                });
            ClubServiceMock
                .Setup(service => service.CanManageClubAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync((int clubId, int userId, string? _) => clubId == ClubId && userId == OwnerUserId);

            ImageRepositoryMock
                .Setup(repository => repository.AddImagesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((int eventId, IEnumerable<string> urls) =>
                {
                    var images = urls
                        .Select((url, index) => new EventImage
                        {
                            EventId = eventId,
                            ImageUrl = url,
                            SortOrder = index
                        })
                        .ToList();

                    Db.EventImages.AddRange(images);
                    Db.SaveChanges();

                    return images;
                });

            RefreshCacheMock
                .Setup(cache => cache.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            CacheMock
                .Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1L);

            // Blob URLs this service issued are recognised, but only the ones registered through
            // RegisterUploadIntent carry a valid intent — mirroring a real presigned upload.
            BlobServiceMock
                .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
                .Returns((string url) => url.StartsWith("https://blob.test/", StringComparison.Ordinal));
            CacheMock
                .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => _uploadIntents.GetValueOrDefault(key));
            BlobServiceMock
                .Setup(service => service.DeleteBlobAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            // The real repository over the same in-memory context, so response building
            // exercises the actual query rather than a stub.
            SeriesRepositoryMock
                .Setup(repository => repository.GetOccurrencesAsync(It.IsAny<int>()))
                .ReturnsAsync((int seriesId) => Db.Events
                    .Include(e => e.Images)
                    .Where(e => e.SeriesId == seriesId)
                    .OrderBy(e => e.OccurrenceIndex)
                    .ToList());

            Service = new EventSeriesService(
                db,
                SeriesRepositoryMock.Object,
                EventsRepositoryMock.Object,
                ImageRepositoryMock.Object,
                ClubServiceMock.Object,
                BlobServiceMock.Object,
                CacheMock.Object,
                RefreshCacheMock.Object,
                OutboxWriterMock.Object,
                Clock);
        }

        /// <param name="utcNow">
        /// Starting point for the fake clock. Tests that assert on "already started" scoping, or
        /// that retime a series, must sit before the occurrences they act on.
        /// </param>
        public static async Task<Harness> CreateAsync(DateTime? utcNow = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            // SQLite enforces foreign keys, so the club an event points at has to exist.
            db.Users.AddRange(
                new backend.main.features.profile.User
                {
                    Id = 7,
                    Email = "organizer@test.local",
                    Usertype = "Organizer"
                },
                new backend.main.features.profile.User
                {
                    Id = 99,
                    Email = "viewer@test.local",
                    Usertype = "Participant"
                });

            db.Clubs.Add(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Board Games Club",
                Description = "A club for tabletop events.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/gaming.png"
            });

            await db.SaveChangesAsync();

            return new Harness(
                connection,
                db,
                utcNow ?? new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        public async Task<EventEntity> AddDraftAsync(
            IEnumerable<string>? imageUrls = null,
            EventLifecycleState lifecycleState = EventLifecycleState.Draft)
        {
            var ev = new EventEntity
            {
                Name = "Weekly Tabletop Night",
                Description = "A regular evening of board games for everyone.",
                Location = "Studio 1",
                ClubId = ClubId,
                LifecycleState = lifecycleState,
                maxParticipants = 30,
                registerCost = 0,
                Category = EventCategory.Other,
                StartTime = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc),
                CurrentVersionNumber = 1,
                CreatedAt = Clock.GetUtcNow().UtcDateTime,
                UpdatedAt = Clock.GetUtcNow().UtcDateTime
            };

            Db.Events.Add(ev);
            await Db.SaveChangesAsync();

            foreach (var (url, index) in (imageUrls ?? ["https://cdn.test/default.png"]).Select((u, i) => (u, i)))
            {
                Db.EventImages.Add(new EventImage
                {
                    EventId = ev.Id,
                    ImageUrl = url,
                    SortOrder = index
                });
            }

            await Db.SaveChangesAsync();
            await Db.Entry(ev).Collection(e => e.Images).LoadAsync();

            return ev;
        }

        public async Task<backend.main.features.events.series.contracts.responses.EventSeriesResponse>
            CreateSeriesAsync(int occurrenceCount, bool publish = false)
        {
            var template = await AddDraftAsync();

            var series = await Service.CreateFromDraftAsync(
                template.Id,
                OwnerUserId,
                OwnerRole,
                Request(occurrenceCount: occurrenceCount));

            if (publish)
            {
                await Service.PublishAsync(series.Id, OwnerUserId, OwnerRole);
                Db.ChangeTracker.Clear();
            }

            return series;
        }

        /// <summary>
        /// Records the upload intent a presigned-URL request would have cached, so the URL
        /// passes validation as an image this organizer genuinely uploaded for this club.
        /// </summary>
        public string RegisterUploadIntent(string publicUrl)
        {
            var intent = new
            {
                ClubId,
                EventId = (int?)null,
                UserId = OwnerUserId,
                PublicUrl = publicUrl,
                ContentType = "image/png"
            };

            _uploadIntents[EventImageUploadValidator.IntentKey(publicUrl)] =
                System.Text.Json.JsonSerializer.Serialize(intent);

            return publicUrl;
        }

        public async Task<EventEntity> OccurrenceAsync(int seriesId, int index) =>
            await Db.Events.FirstAsync(e => e.SeriesId == seriesId && e.OccurrenceIndex == index);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>Controllable clock, so "already started" scoping is testable without waiting.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeClock(DateTime utcNow) => _now = new DateTimeOffset(utcNow);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
