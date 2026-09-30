using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.media;
using backend.main.features.media.contracts.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.tests.Integration.Features.Storage;

/// <summary>
/// Presigned uploads with <c>storage.quarantine</c> on (the default): bytes land in a private
/// container and reach a public URL only once they have been validated and re-encoded, and
/// <c>GET /api/media/{publicId}</c> reports where an upload is in that.
/// </summary>
public class MediaAssetEndpointsTests
{
    [Fact]
    public async Task PresignedUpload_ShouldTargetQuarantine_AndPublishNothingUntilAttached()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-mint@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Quarantine Mint Club");

        var upload = await PresignAsync(app, session.AccessToken, club.Id);

        upload.MediaAssetId.Should().NotBeNull();
        upload.UploadUrl.Should().StartWith("https://storage.test/event-assets-quarantine/");
        upload.PublicUrl.Should().StartWith("https://storage.test/event-assets/").And.EndWith(".webp");
        app.BlobStorage.UploadedImages.Should().NotContainKey(upload.PublicUrl,
            "nothing a client uploads is publicly readable before it has been validated");

        var asset = await FindAssetAsync(app, upload.MediaAssetId!.Value);
        asset.Status.Should().Be(MediaAssetStatus.PendingUpload);
        asset.ClubId.Should().Be(club.Id);
    }

    [Fact]
    public async Task AttachingAQuarantinedImage_ShouldPublishItReencoded_WithTheGpsStripped()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-attach@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Quarantine Attach Club");
        var ev = await CreateEventAsync(app, session.AccessToken, club.Id, "Quarantine Attach Event");

        var upload = await PresignAsync(app, session.AccessToken, club.Id, ev.Id, "photo.jpg", "image/jpeg");
        var quarantinePath = app.BlobStorage.QuarantinePathFor(upload.PublicUrl);
        app.BlobStorage.StageQuarantineUpload(upload.PublicUrl, JpegWithGps(), "image/jpeg");

        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = upload.PublicUrl, altText = "A photo" })));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));

        var published = app.BlobStorage.UploadedImages[upload.PublicUrl];
        published.ContentType.Should().Be("image/webp");
        var stored = Image.Identify(published.Content);
        stored.Metadata.ExifProfile.Should().BeNull("GPS must not survive into a public URL");
        app.BlobStorage.Quarantine.Should().NotContainKey(quarantinePath);

        var asset = await FindAssetAsync(app, upload.MediaAssetId!.Value);
        asset.Status.Should().Be(MediaAssetStatus.Ready);
        asset.ValidatedAt.Should().NotBeNull();
        asset.ContentType.Should().Be("image/webp");
        (await app.QueryDbAsync(db => db.EventImages.AnyAsync(i => i.EventId == ev.Id && i.ImageUrl == upload.PublicUrl)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AttachingRejectedBytes_ShouldLeaveNoBlobInEitherContainer_AndNoImageRow()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-reject@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Quarantine Reject Club");
        var ev = await CreateEventAsync(app, session.AccessToken, club.Id, "Quarantine Reject Event");
        var imagesBefore = await app.QueryDbAsync(db => db.EventImages.CountAsync(i => i.EventId == ev.Id));

        var upload = await PresignAsync(app, session.AccessToken, club.Id, ev.Id);
        var quarantinePath = app.BlobStorage.QuarantinePathFor(upload.PublicUrl);
        app.BlobStorage.StageQuarantineUpload(
            upload.PublicUrl, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]);

        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = upload.PublicUrl })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        app.BlobStorage.UploadedImages.Should().NotContainKey(upload.PublicUrl);
        app.BlobStorage.Quarantine.Should().NotContainKey(quarantinePath);

        var asset = await FindAssetAsync(app, upload.MediaAssetId!.Value);
        asset.Status.Should().Be(MediaAssetStatus.Rejected);
        asset.RejectionReason.Should().Be("Only JPEG, PNG, WEBP, and GIF images are supported.");

        // Public pages never see a non-Ready image because a rejected one never reaches a row.
        (await app.QueryDbAsync(db => db.EventImages.CountAsync(i => i.EventId == ev.Id)))
            .Should().Be(imagesBefore);
    }

    [Fact]
    public async Task AttachingAnAnimatedGif_ShouldBeRejected_WithTheReasonRecorded()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-animated@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Quarantine Animated Club");
        var ev = await CreateEventAsync(app, session.AccessToken, club.Id, "Quarantine Animated Event");

        var upload = await PresignAsync(app, session.AccessToken, club.Id, ev.Id, "loop.gif", "image/gif");
        app.BlobStorage.StageQuarantineUpload(upload.PublicUrl, AnimatedGif(), "image/gif");

        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = upload.PublicUrl })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FindAssetAsync(app, upload.MediaAssetId!.Value)).RejectionReason
            .Should().Be("Animated images are not supported. Upload a single-frame image.");
    }

    [Fact]
    public async Task ClubCreate_ShouldPublishAQuarantinedIcon()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-club@example.com");

        var club = await CreateClubAsync(app, session.AccessToken, "Quarantine Icon Club");

        club.ClubImage.Should().EndWith(".webp");
        app.BlobStorage.UploadedImages.Should().ContainKey(club.ClubImage);
        (await app.QueryDbAsync(db => db.MediaAssets.SingleAsync(a => a.PublicUrl == club.ClubImage)))
            .Status.Should().Be(MediaAssetStatus.Ready);
    }

    [Fact]
    public async Task AttachingAnUploadIssuedForAnotherClub_ShouldBeRefused_BeforeItsBytesAreProcessed()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "quarantine-scope@example.com");
        var clubA = await CreateClubAsync(app, session.AccessToken, "Quarantine Scope Club A");
        var clubB = await CreateClubAsync(app, session.AccessToken, "Quarantine Scope Club B");
        var eventInB = await CreateEventAsync(app, session.AccessToken, clubB.Id, "Quarantine Scope Event");

        var issuedForA = await PresignAsync(app, session.AccessToken, clubA.Id);

        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{eventInB.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = issuedForA.PublicUrl })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FindAssetAsync(app, issuedForA.MediaAssetId!.Value)).Status.Should().Be(MediaAssetStatus.PendingUpload);
        app.BlobStorage.Quarantine.Should().ContainKey(app.BlobStorage.QuarantinePathFor(issuedForA.PublicUrl));
    }

    [Fact]
    public async Task GetMediaAsset_ShouldTrackAnUploadFromPendingToReady_ForItsUploader()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "media-status-owner@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Media Status Club");
        var ev = await CreateEventAsync(app, session.AccessToken, club.Id, "Media Status Event");
        var upload = await PresignAsync(app, session.AccessToken, club.Id, ev.Id);

        var pending = await GetStatusAsync(app, session.AccessToken, upload.MediaAssetId!.Value);
        pending.Status.Should().Be(MediaAssetStatus.PendingUpload);
        pending.Url.Should().BeNull();

        var attach = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = upload.PublicUrl })));
        attach.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(attach));

        var ready = await GetStatusAsync(app, session.AccessToken, upload.MediaAssetId!.Value);
        ready.Id.Should().Be(upload.MediaAssetId!.Value);
        ready.Status.Should().Be(MediaAssetStatus.Ready);
        ready.Url.Should().Be(upload.PublicUrl);
        ready.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task GetMediaAsset_ShouldReportTheReason_ForARejectedUpload()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "media-status-rejected@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Media Rejected Club");
        var ev = await CreateEventAsync(app, session.AccessToken, club.Id, "Media Rejected Event");
        var upload = await PresignAsync(app, session.AccessToken, club.Id, ev.Id);
        app.BlobStorage.StageQuarantineUpload(upload.PublicUrl, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B]);

        await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{ev.Id}/images",
            session.AccessToken,
            JsonContent.Create(new { imageUrl = upload.PublicUrl })));

        var rejected = await GetStatusAsync(app, session.AccessToken, upload.MediaAssetId!.Value);
        rejected.Status.Should().Be(MediaAssetStatus.Rejected);
        rejected.RejectionReason.Should().Be("Only JPEG, PNG, WEBP, and GIF images are supported.");
        rejected.Url.Should().BeNull();
    }

    [Fact]
    public async Task GetMediaAsset_ShouldShowAClubManager_ButAnswerNotFoundToEveryoneElse()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var ownerSession = await SignUpAsync(app, "media-status-club-owner@example.com");
        var managerSession = await SignUpAsync(app, "media-status-manager@example.com");
        var strangerSession = await SignUpAsync(app, "media-status-stranger@example.com");
        var owner = (await app.FindUserByEmailAsync("media-status-club-owner@example.com"))!;
        var manager = (await app.FindUserByEmailAsync("media-status-manager@example.com"))!;

        var club = await CreateClubAsync(app, ownerSession.AccessToken, "Media Managed Club");
        await app.AddClubStaffAsync(club.Id, manager.Id, owner.Id, ClubStaffRole.Manager);
        var upload = await PresignAsync(app, ownerSession.AccessToken, club.Id);

        var asManager = await app.GetWithBearerAsync($"/api/media/{upload.MediaAssetId}", managerSession.AccessToken);
        asManager.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(asManager));

        // 404, not 403: a stranger must not learn that the id exists.
        var asStranger = await app.GetWithBearerAsync($"/api/media/{upload.MediaAssetId}", strangerSession.AccessToken);
        asStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknown = await app.GetWithBearerAsync($"/api/media/{Guid.NewGuid()}", strangerSession.AccessToken);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await asStranger.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetMediaAsset_ShouldRequireSignIn()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.GetAsync($"/api/media/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMediaAsset_ShouldNotExist_WhenQuarantineIsOff()
    {
        await using var app = await AuthApiTestApp.CreateWithoutQuarantineAsync();
        var session = await SignUpAsync(app, "media-status-flag-off@example.com");
        var club = await CreateClubAsync(app, session.AccessToken, "Media Flag Off Club");

        var upload = await PresignAsync(app, session.AccessToken, club.Id);
        upload.MediaAssetId.Should().BeNull("with quarantine off there is nothing to poll");
        upload.PublicUrl.Should().EndWith(".png", "the pre-quarantine upload keeps the uploader's format");

        var response = await app.GetWithBearerAsync($"/api/media/{Guid.NewGuid()}", session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<MediaAssetResponse> GetStatusAsync(AuthApiTestApp app, string accessToken, Guid mediaAssetId)
    {
        var response = await app.GetWithBearerAsync($"/api/media/{mediaAssetId}", accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<MediaAssetResponse>(response)).Data!;
    }

    private static async Task<AuthenticatedSessionResponse> SignUpAsync(AuthApiTestApp app, string email) =>
        await app.SignUpAndVerifyByTokenAsync(email, role: "Organizer", transport: SessionTransportResolver.ApiValue);

    private static async Task<PresignedUploadResponse> PresignAsync(
        AuthApiTestApp app,
        string accessToken,
        int clubId,
        int? eventId = null,
        string fileName = "poster.png",
        string contentType = "image/png")
    {
        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new { clubId, eventId, fileName, contentType })));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<PresignedUploadResponse>(response)).Data!;
    }

    private static async Task<ClubModel> CreateClubAsync(AuthApiTestApp app, string accessToken, string name)
    {
        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Quarantine testing group",
                Clubtype = "social",
                ClubImageUrl = await app.CreateClubImageUrlAsync(accessToken)
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<ClubModel>(response)).Data!;
    }

    private static async Task<EventResponse> CreateEventAsync(AuthApiTestApp app, string accessToken, int clubId, string name)
    {
        var image = await PresignAsync(app, accessToken, clubId);
        var response = await app.Client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/events/{clubId}",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "An event for exercising quarantined uploads end to end.",
                location = "Student Center",
                imageUrls = new[] { image.PublicUrl },
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(6),
                endTime = DateTime.UtcNow.AddDays(6).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room A",
                city = "Toronto",
                tags = new[] { "testing" }
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<EventResponse>(response)).Data!;
    }

    private static Task<MediaAsset> FindAssetAsync(AuthApiTestApp app, Guid publicId) =>
        app.QueryDbAsync(db => db.MediaAssets.AsNoTracking().SingleAsync(a => a.PublicId == publicId));

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static byte[] JpegWithGps()
    {
        using var image = new Image<Rgba32>(64, 48, new Rgba32(120, 160, 200));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.Metadata.ExifProfile.SetValue(
            ExifTag.GPSLatitude,
            [new Rational(43, 1), new Rational(39, 1), new Rational(12, 1)]);

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] AnimatedGif()
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0));
        using (var second = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 255)))
            image.Frames.AddFrame(second.Frames.RootFrame);

        using var stream = new MemoryStream();
        image.SaveAsGif(stream);
        return stream.ToArray();
    }

    private sealed class ClubModel
    {
        public int Id
        {
            get; init;
        }

        public string ClubImage { get; init; } = string.Empty;
    }
}
