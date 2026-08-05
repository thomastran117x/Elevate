using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;
using backend.main.features.events.series;
using backend.main.features.events.series.contracts.responses;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Events;

/// <summary>
/// End-to-end coverage for recurrence series against real MySQL.
/// <para>
/// The time zone assertions here are the ones unit tests cannot make: they prove the UTC
/// instants survive the round trip through a naive <c>datetime(6)</c> column without Pomelo or
/// the MySQL session zone shifting them.
/// </para>
/// </summary>
public class EventSeriesEndpointsTests
{
    private const string NewYork = "America/New_York";

    [Fact]
    public async Task SeriesEndpoints_ShouldPreviewCreatePublishAndListOccurrencesAsNormalEvents()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-owner@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Recurring Events Club");

        // ── preview ──────────────────────────────────────────────────────────────
        var previewResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/series/preview",
            organizer.AccessToken,
            JsonContent.Create(new { recurrence = Recurrence(occurrenceCount: 3) })));

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await app.ReadApiResponseAsync<EventSeriesPreviewResponse>(previewResponse)).Data!;
        preview.OccurrenceCount.Should().Be(3);
        preview.Occurrences.Should().HaveCount(3);

        // Nothing was persisted by a preview.
        (await app.QueryDbAsync(db => db.EventSeries.CountAsync())).Should().Be(0);

        // ── create ───────────────────────────────────────────────────────────────
        var draft = await CreateDraftAsync(app, organizer.AccessToken, club.Id);

        var createResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/series",
            organizer.AccessToken,
            JsonContent.Create(new { recurrence = Recurrence(occurrenceCount: 3) })));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var series = (await app.ReadApiResponseAsync<EventSeriesResponse>(createResponse)).Data!;

        series.Occurrences.Should().HaveCount(3);
        series.Occurrences.Should().OnlyContain(o => o.LifecycleState == EventLifecycleState.Draft);
        series.Occurrences[0].Id.Should().Be(draft.Id, "the template becomes occurrence 0");

        // ── get ──────────────────────────────────────────────────────────────────
        var getResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/series/{series.Id}",
            organizer.AccessToken));

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<EventSeriesResponse>(getResponse)).Data!
            .Occurrences.Should().HaveCount(3);

        // ── list by club ─────────────────────────────────────────────────────────
        var listResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/clubs/{club.Id}/series?page=1&pageSize=20",
            organizer.AccessToken));

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<PagedResponse<EventSeriesSummaryResponse>>(listResponse)).Data!
            .Items.Should().ContainSingle().Which.Id.Should().Be(series.Id);

        // ── publish ──────────────────────────────────────────────────────────────
        var publishResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/series/{series.Id}/publish",
            organizer.AccessToken));

        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var publishResult = (await app.ReadApiResponseAsync<EventSeriesBulkResultResponse>(publishResponse)).Data!;
        publishResult.AffectedCount.Should().Be(3);

        var persisted = await app.QueryDbAsync(db => db.Events
            .Where(e => e.SeriesId == series.Id)
            .ToListAsync());

        persisted.Should().HaveCount(3);
        persisted.Should().OnlyContain(e => e.LifecycleState == EventLifecycleState.Published);

        // ── occurrences behave as ordinary events on the public listing ───────────
        var publicList = await app.Client.GetAsync("/api/events?page=1&pageSize=50");
        publicList.StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = (await app.ReadApiResponseAsync<PagedResponse<EventResponse>>(publicList)).Data!;
        var listedIds = listed.Items.Select(e => e.Id).ToList();

        foreach (var occurrence in persisted)
            listedIds.Should().Contain(occurrence.Id, "an occurrence is an ordinary event");

        listed.Items
            .Where(e => e.SeriesId == series.Id)
            .Should().OnlyContain(e => e.OccurrenceIndex != null && e.TimeZoneId == NewYork);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldPersistUtcInstantsThatHoldTheWallClockAcrossDst()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-dst@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "DST Club");
        var draft = await CreateDraftAsync(app, organizer.AccessToken, club.Id);

        // Weekly 7pm in New York from 2026-03-03, spanning the 2026-03-08 spring-forward.
        var createResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/series",
            organizer.AccessToken,
            JsonContent.Create(new
            {
                recurrence = Recurrence(
                    occurrenceCount: 2,
                    startLocal: "2026-03-03T19:00",
                    timeZoneId: NewYork)
            })));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var series = (await app.ReadApiResponseAsync<EventSeriesResponse>(createResponse)).Data!;

        var stored = await app.QueryDbAsync(db => db.Events
            .Where(e => e.SeriesId == series.Id)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync());

        // Both occurrences read 7pm locally, which is a different UTC instant on each side of
        // the transition. This is the assertion that would fail if the column round trip, the
        // connector, or the MySQL session zone shifted the value.
        stored[0].StartTime.Should().Be(new DateTime(2026, 3, 4, 0, 0, 0));
        stored[1].StartTime.Should().Be(new DateTime(2026, 3, 10, 23, 0, 0));

        (stored[1].StartTime!.Value - stored[0].StartTime!.Value)
            .Should().Be(TimeSpan.FromDays(7) - TimeSpan.FromHours(1));

        stored.Should().OnlyContain(e => e.TimeZoneId == NewYork);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldUpdateASingleOccurrenceWithoutTouchingItsSiblings()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-single@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Single Edit Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3);

        var target = series.Occurrences[1];

        // Editing one occurrence needs no series endpoint — it is an ordinary event.
        var patchResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/{target.Id}/draft",
            organizer.AccessToken,
            JsonContent.Create(new { location = "Just This One Hall" })));

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var all = await app.QueryDbAsync(db => db.Events
            .Where(e => e.SeriesId == series.Id)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync());

        all[1].Location.Should().Be("Just This One Hall");
        all[0].Location.Should().NotBe("Just This One Hall");
        all[2].Location.Should().NotBe("Just This One Hall");

        // It is now flagged, so a later series-wide update leaves it alone by default.
        all[1].SeriesOverridden.Should().BeTrue();
        all[0].SeriesOverridden.Should().BeFalse();
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldUpdateThisAndAllFutureOccurrencesOnly()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-future@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Future Update Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 4);

        var pivot = series.Occurrences[2];

        var patchResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/series/{series.Id}/occurrences",
            organizer.AccessToken,
            JsonContent.Create(new { fromEventId = pivot.Id, location = "Relocated Hall" })));

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await app.ReadApiResponseAsync<EventSeriesBulkResultResponse>(patchResponse)).Data!;
        result.AffectedCount.Should().Be(2);

        var all = await app.QueryDbAsync(db => db.Events
            .Where(e => e.SeriesId == series.Id)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync());

        all[0].Location.Should().NotBe("Relocated Hall", "occurrences before the pivot are untouched");
        all[1].Location.Should().NotBe("Relocated Hall");
        all[2].Location.Should().Be("Relocated Hall");
        all[3].Location.Should().Be("Relocated Hall");
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldCancelOneOccurrenceWithoutCancellingTheSeries()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-cancel-one@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Cancel One Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3, publish: true);

        var victim = series.Occurrences[1];

        var cancelResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{victim.Id}/cancel",
            organizer.AccessToken));

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var all = await app.QueryDbAsync(db => db.Events
            .Where(e => e.SeriesId == series.Id)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync());

        // The acceptance criterion: one cancelled occurrence, series and siblings intact.
        all.Should().HaveCount(3);
        all[1].LifecycleState.Should().Be(EventLifecycleState.Cancelled);
        all[0].LifecycleState.Should().Be(EventLifecycleState.Published);
        all[2].LifecycleState.Should().Be(EventLifecycleState.Published);

        var seriesRow = await app.QueryDbAsync(db => db.EventSeries.SingleAsync(s => s.Id == series.Id));
        seriesRow.Status.Should().Be(EventSeriesStatus.Active);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldDeleteOneOccurrenceWithoutDeletingTheSeries()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-delete-one@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Delete One Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3);

        var victim = series.Occurrences[2];

        var deleteResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/{victim.Id}",
            organizer.AccessToken));

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.EventSeries.CountAsync(s => s.Id == series.Id)))
            .Should().Be(1, "deleting an occurrence must never remove its series");

        (await app.QueryDbAsync(db => db.Events.CountAsync(e => e.SeriesId == series.Id)))
            .Should().Be(2);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldDetachAnOccurrenceAndExcludeItFromFutureSeriesUpdates()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-detach@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Detach Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3);

        var detachTarget = series.Occurrences[2];

        var detachResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{detachTarget.Id}/series/detach",
            organizer.AccessToken));

        detachResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detached = (await app.ReadApiResponseAsync<ManagedEventResponse>(detachResponse)).Data!;
        detached.SeriesId.Should().BeNull();

        // It survives as an ordinary event and is out of scope for series-wide updates.
        var patchResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/events/series/{series.Id}/occurrences",
            organizer.AccessToken,
            JsonContent.Create(new { fromEventId = series.Occurrences[0].Id, location = "Series Hall" })));

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<EventSeriesBulkResultResponse>(patchResponse)).Data!
            .AffectedCount.Should().Be(2);

        var stillThere = await app.QueryDbAsync(db => db.Events.SingleAsync(e => e.Id == detachTarget.Id));
        stillThere.SeriesId.Should().BeNull();
        stillThere.Location.Should().NotBe("Series Hall");
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldExtendASeries()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-extend@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Extend Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3);

        var extendResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/series/{series.Id}/extend",
            organizer.AccessToken,
            JsonContent.Create(new { occurrenceCount = 5 })));

        extendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<EventSeriesResponse>(extendResponse)).Data!
            .Occurrences.Should().HaveCount(5);

        (await app.QueryDbAsync(db => db.Events.CountAsync(e => e.SeriesId == series.Id)))
            .Should().Be(5);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldCancelAndThenDeleteAWholeSeries()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-teardown@example.com", "Organizer");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Teardown Club");
        var series = await CreateSeriesAsync(app, organizer.AccessToken, club.Id, occurrenceCount: 3, publish: true);

        var cancelResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/series/{series.Id}/cancel",
            organizer.AccessToken,
            JsonContent.Create(new { futureOnly = true })));

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadApiResponseAsync<EventSeriesBulkResultResponse>(cancelResponse)).Data!
            .AffectedCount.Should().Be(3);

        // Cancelling never deletes.
        (await app.QueryDbAsync(db => db.Events.CountAsync(e => e.SeriesId == series.Id))).Should().Be(3);

        var deleteResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/events/series/{series.Id}",
            organizer.AccessToken,
            JsonContent.Create(new { scope = "SeriesRecordOnly" })));

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.QueryDbAsync(db => db.EventSeries.CountAsync(s => s.Id == series.Id))).Should().Be(0);

        // The occurrences outlive the series row, detached rather than cascaded away.
        var orphans = await app.QueryDbAsync(db => db.Events
            .Where(e => e.ClubId == club.Id)
            .ToListAsync());

        orphans.Should().HaveCount(3);
        orphans.Should().OnlyContain(e => e.SeriesId == null);
    }

    [Fact]
    public async Task SeriesEndpoints_ShouldRejectUnknownTimeZonesAndNonManagers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (organizer, _) = await CreateUserSessionAsync(app, "series-guard-owner@example.com", "Organizer");
        var (outsider, _) = await CreateUserSessionAsync(app, "series-guard-outsider@example.com", "Participant");
        var club = await CreateClubAsync(app, organizer.AccessToken, "Guarded Club");

        var badZone = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/series/preview",
            organizer.AccessToken,
            JsonContent.Create(new { recurrence = Recurrence(timeZoneId: "Mars/Olympus_Mons") })));

        badZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A Windows zone id would resolve on a Windows box but not a Linux pod, so it is refused.
        var windowsZone = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/series/preview",
            organizer.AccessToken,
            JsonContent.Create(new { recurrence = Recurrence(timeZoneId: "Eastern Standard Time") })));

        windowsZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var forbidden = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{club.Id}/series/preview",
            outsider.AccessToken,
            JsonContent.Create(new { recurrence = Recurrence() })));

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>
    /// Enums are sent as strings on purpose: that is what the Angular client posts, and passing
    /// C# enum values here would serialize them as numbers and quietly exercise a different
    /// binding path than production traffic takes.
    /// </summary>
    private static object Recurrence(
        int occurrenceCount = 3,
        string startLocal = "2027-06-01T19:00",
        string timeZoneId = NewYork) => new
        {
            frequency = "Weekly",
            interval = 1,
            startLocalDateTime = startLocal,
            durationMinutes = 120,
            timeZoneId,
            endMode = "Count",
            occurrenceCount
        };

    private static async Task<EventSeriesResponse> CreateSeriesAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        int occurrenceCount,
        bool publish = false)
    {
        var draft = await CreateDraftAsync(app, accessToken, clubId);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{draft.Id}/series",
            accessToken,
            JsonContent.Create(new { recurrence = Recurrence(occurrenceCount) })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var series = (await app.ReadApiResponseAsync<EventSeriesResponse>(response)).Data!;

        if (!publish)
            return series;

        var publishResponse = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/series/{series.Id}/publish",
            accessToken));

        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/events/series/{series.Id}",
            accessToken));

        return (await app.ReadApiResponseAsync<EventSeriesResponse>(refreshed)).Data!;
    }

    private static async Task<ManagedEventResponse> CreateDraftAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId)
    {
        var image = await CreatePendingImageAsync(app, accessToken, clubId);

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/clubs/{clubId}/drafts",
            accessToken,
            JsonContent.Create(new
            {
                name = "Weekly Tabletop",
                description = "A recurring evening of board games for everyone.",
                location = "Studio 1",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = new DateTime(2027, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                endTime = new DateTime(2027, 6, 1, 11, 0, 0, DateTimeKind.Utc),
                category = EventCategory.Other,
                venueName = "North Hall",
                city = "Toronto",
                tags = new[] { "games" }
            })));

        await ThrowOnUnexpectedStatusAsync(app, response, HttpStatusCode.Created);

        return (await app.ReadApiResponseAsync<ManagedEventResponse>(response)).Data!;
    }

    private static async Task<(AuthenticatedSessionResponse Session, backend.main.features.profile.User? User)>
        CreateUserSessionAsync(AuthApiTestApp app, string email, string role = "Participant")
    {
        var session = await app.SignUpAndVerifyByTokenAsync(email, "Str0ng!Passw0rd", role);
        var user = await app.QueryDbAsync(db => db.Users.FirstOrDefaultAsync(u => u.Email == email));

        return (session, user);
    }

    private static async Task<ClubApiModel> CreateClubAsync(AuthApiTestApp app, string accessToken, string name)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "A club used by the recurrence integration tests.",
                // Lowercase: ClubCreateRequest validates Clubtype against a fixed list of names.
                Clubtype = "gaming",
                // Must be a URL the fake blob storage owns, or the request is rejected.
                ClubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png"),
                Email = $"{name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}@example.com"
            })));

        await ThrowOnUnexpectedStatusAsync(app, response, HttpStatusCode.Created);

        return (await app.ReadApiResponseAsync<ClubApiModel>(response)).Data!;
    }

    /// <summary>
    /// Surfaces the server's own diagnostics when a fixture request fails. A bare status-code
    /// assertion here says only "400" and hides which field the API objected to, which turns a
    /// broken fixture into a guessing game.
    /// </summary>
    private static async Task ThrowOnUnexpectedStatusAsync(
        AuthApiTestApp app,
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        throw new Xunit.Sdk.XunitException(await app.DescribeFailureAsync(response));
    }

    private static async Task<PresignedUploadResponse> CreatePendingImageAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new
            {
                clubId,
                fileName = "cover.png",
                contentType = "image/png"
            })));

        await ThrowOnUnexpectedStatusAsync(app, response, HttpStatusCode.OK);

        return (await app.ReadApiResponseAsync<PresignedUploadResponse>(response)).Data!;
    }

    private sealed class ClubApiModel
    {
        public int Id
        {
            get; init;
        }
        public string Name { get; init; } = string.Empty;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (content != null)
            request.Content = content;

        return request;
    }
}
