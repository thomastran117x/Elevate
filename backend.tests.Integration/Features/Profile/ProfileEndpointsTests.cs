using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.application.security;
using backend.main.features.clubs;
using backend.main.features.clubs.versions;
using backend.main.features.events.images;

using EventEntity = backend.main.features.events.Events;
using backend.main.features.profile.contracts.requests;
using backend.main.features.profile.contracts.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.tests.Integration.Features.Profile;

public class ProfileEndpointsTests
{
    [Fact]
    public async Task GetUpdateAndPublicProfile_ShouldReflectChanges()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("profile-user@example.com", role: "Organizer");
        await app.SeedKnownDeviceAsync(user.Id, "profile-user-device");
        var session = await app.LoginApiAsync(
            "profile-user",
            trustedDeviceToken: "profile-user-device");

        // GET /api/profile
        var getResponse = await app.GetWithBearerAsync("/api/profile", session.AccessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await app.ReadApiResponseAsync<MyProfileResponse>(getResponse);
        initial.Data!.Email.Should().Be("profile-user@example.com");
        initial.Data.Phone.Should().BeNull();
        initial.Data.CanChangeUsername.Should().BeTrue();
        var expectedRole = initial.Data.Usertype;

        // PATCH /api/profile
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/profile")
        {
            Content = JsonContent.Create(new
            {
                name = "Profile User",
                phone = "+1 416 555 0100",
                address = "1 Test Street",
                username = "bypass-attempt"
            })
        };
        await AddAuthAndCsrfAsync(app, patchRequest, session.AccessToken);
        var patchResponse = await app.Client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(patchResponse);
        updated.Data!.Name.Should().Be("Profile User");
        // Unknown username input cannot bypass the dedicated MFA-protected route.
        updated.Data.Username.Should().Be("profile-user");
        updated.Data.Phone.Should().Be("+1 416 555 0100");
        updated.Data.Address.Should().Be("1 Test Street");
        // Identity and role must be preserved (never mutated via a profile update).
        updated.Data.Email.Should().Be("profile-user@example.com");
        updated.Data.Usertype.Should().Be(expectedRole);

        await app.CompleteSessionMfaByEmailAsync(
            "profile-user@example.com",
            session.AccessToken);
        var usernameRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/profile/username")
        {
            Content = JsonContent.Create(new ChangeUsernameRequest
            {
                Username = "  ProfileUser  "
            })
        };
        await AddAuthAndCsrfAsync(app, usernameRequest, session.AccessToken);
        var usernameResponse = await app.Client.SendAsync(usernameRequest);
        usernameResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        updated = await app.ReadApiResponseAsync<MyProfileResponse>(usernameResponse);
        updated.Data!.Username.Should().Be("profileuser");
        updated.Data.CanChangeUsername.Should().BeFalse();
        updated.Data.UsernameChangeAvailableAtUtc.Should().NotBeNull();

        // The current session remains valid and the new username becomes the login identifier.
        (await app.GetWithBearerAsync("/api/profile", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await app.LoginApiAsync(
            "PROFILEUSER",
            trustedDeviceToken: "profile-user-device");

        var oldLogin = await app.PostJsonWithCsrfAsync("/api/auth/login", new
        {
            username = "profile-user",
            password = "Password123!",
            captcha = "captcha",
            transport = "api"
        });
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // GET /api/profile/{username} (public, anonymous) — public fields only, no PII.
        var username = "profileuser";
        var publicResponse = await app.Client.GetAsync($"/api/profile/{username}");
        publicResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicProfile = await app.ReadApiResponseAsync<PublicProfileResponse>(publicResponse);
        publicProfile.Data!.Username.Should().Be("profileuser");
        publicProfile.Data.Name.Should().Be("Profile User");
        publicProfile.Data.Usertype.Should().Be(expectedRole);
        (await publicResponse.Content.ReadAsStringAsync())
            .Should().NotContain("profile-user@example.com")
            .And.NotContain("1 Test Street");

        // The reserved old handle resolves to the current canonical profile during cooldown.
        var oldPublicResponse = await app.Client.GetAsync("/api/profile/profile-user");
        oldPublicResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var oldPublicProfile = await app.ReadApiResponseAsync<PublicProfileResponse>(oldPublicResponse);
        oldPublicProfile.Data!.Username.Should().Be("profileuser");
    }

    [Fact]
    public async Task UploadAvatar_ShouldStoreAvatarUrl()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-device");
        var session = await app.LoginApiAsync(
            "avatar-user",
            trustedDeviceToken: "avatar-device");

        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalPng);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(file, "image", "avatar.png");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/profile/avatar")
        {
            Content = multipart
        };
        await AddAuthAndCsrfAsync(app, request, session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(response);
        updated.Data!.Avatar.Should().NotBeNullOrWhiteSpace();
        // Stored as what the pipeline produced, not as what the uploader named it.
        updated.Data.Avatar!.Should().EndWith(".webp").And.NotContain("avatar.png");
        app.BlobStorage.UploadedImages[updated.Data.Avatar].ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task UploadAvatar_ShouldStripGpsExifBeforeStoring()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-gps-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-gps-device");
        var session = await app.LoginApiAsync("avatar-gps-user", trustedDeviceToken: "avatar-gps-device");

        var photo = new Image<Rgba32>(64, 48, new Rgba32(200, 120, 40));
        photo.Metadata.ExifProfile = new ExifProfile();
        photo.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        photo.Metadata.ExifProfile.SetValue(
            ExifTag.GPSLatitude,
            [new Rational(43, 1), new Rational(39, 1), new Rational(12, 1)]);
        var jpeg = EncodeImage(photo, (image, stream) => image.SaveAsJpeg(stream));

        var response = await PostAvatarAsync(app, session.AccessToken, jpeg, "image/jpeg", "photo.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(response);
        var stored = app.BlobStorage.UploadedImages[updated.Data!.Avatar!];
        Image.Identify(stored.Content).Metadata.ExifProfile.Should().BeNull();
    }

    [Fact]
    public async Task UploadAvatar_ShouldStoreALargePhotoAt512PixelsAsWebp()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-large-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-large-device");
        var session = await app.LoginApiAsync("avatar-large-user", trustedDeviceToken: "avatar-large-device");

        var jpeg = EncodeImage(
            new Image<Rgba32>(4000, 3000, new Rgba32(30, 60, 90)),
            (image, stream) => image.SaveAsJpeg(stream));

        var response = await PostAvatarAsync(app, session.AccessToken, jpeg, "image/jpeg", "large.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(response);
        var stored = app.BlobStorage.UploadedImages[updated.Data!.Avatar!];
        stored.ContentType.Should().Be("image/webp");
        var info = Image.Identify(stored.Content);
        Math.Max(info.Width, info.Height).Should().Be(512);
    }

    [Fact]
    public async Task UploadAvatar_ShouldRejectAnimatedGifWithAClearMessage()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-gif-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-gif-device");
        var session = await app.LoginApiAsync("avatar-gif-user", trustedDeviceToken: "avatar-gif-device");

        var animation = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0));
        using (var second = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 255)))
        {
            animation.Frames.AddFrame(second.Frames.RootFrame);
        }
        var gif = EncodeImage(animation, (image, stream) => image.SaveAsGif(stream));

        var response = await PostAvatarAsync(app, session.AccessToken, gif, "image/gif", "wave.gif");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Animated images are not supported");
        app.BlobStorage.UploadedImages.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAvatar_ShouldAcceptARealImageWithNoFileExtension()
    {
        // A Blob appended to FormData is sent with the file name "blob" and no extension. The
        // bytes decide, so this has to succeed; an extension allowlist on the DTO would reject it
        // during model validation before [ImageContent] ever saw the file.
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-blob-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-blob-device");
        var session = await app.LoginApiAsync(
            "avatar-blob-user",
            trustedDeviceToken: "avatar-blob-device");

        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalPng);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file, "image", "blob");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/profile/avatar")
        {
            Content = multipart
        };
        await AddAuthAndCsrfAsync(app, request, session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(response);
        updated.Data!.Avatar.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UploadAvatar_ShouldRejectFilesWhoseBytesAreNotAnImage()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-bytes-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-bytes-device");
        var session = await app.LoginApiAsync(
            "avatar-bytes-user",
            trustedDeviceToken: "avatar-bytes-device");

        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 0x1, 0x2, 0x3, 0x4 });
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(file, "image", "avatar.png");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/profile/avatar")
        {
            Content = multipart
        };
        await AddAuthAndCsrfAsync(app, request, session.AccessToken);
        var response = await app.Client.SendAsync(request);

        // The rejection comes from [ImageContent] during model validation, before the image
        // processor or the blob service is reached.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("must be a JPEG, PNG, WEBP, or GIF image");

        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        await AddAuthAndCsrfAsync(app, profileRequest, session.AccessToken);
        var profileResponse = await app.Client.SendAsync(profileRequest);
        var profile = await app.ReadApiResponseAsync<MyProfileResponse>(profileResponse);

        profile.Data!.Avatar.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChangePassword_ShouldSucceed_AndAllowLoginWithNewPassword()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("pw-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pw-device");
        var session = await app.LoginApiAsync(
            "pw-user",
            trustedDeviceToken: "pw-device");
        await app.CompleteSessionMfaByEmailAsync("pw-user@example.com", session.AccessToken);

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/profile/change-password",
            new ChangePasswordAuthenticatedRequest
            {
                CurrentPassword = "Password123!",
                NewPassword = "NewPassword456!"
            },
            session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Password changed");

        // The new password is now in effect.
        var newSession = await app.LoginApiAsync(
            "pw-user",
            password: "NewPassword456!",
            trustedDeviceToken: "pw-device");
        newSession.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeleteAccount_ShouldRemoveUser()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("delete-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "delete-device");
        var session = await app.LoginApiAsync(
            "delete-user",
            trustedDeviceToken: "delete-device");
        await app.CompleteSessionMfaByEmailAsync("delete-user@example.com", session.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/profile");
        await AddAuthAndCsrfAsync(app, request, session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Account deleted");
        (await app.FindUserByEmailAsync("delete-user@example.com")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAccount_ShouldDeleteAvatarAndCascadedResourceBlobs()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("blob-delete@example.com", role: "Organizer");
        await app.SeedKnownDeviceAsync(user.Id, "blob-delete-device");

        // Seed the user's avatar plus a club (with a version) and an event with images —
        // every one of these carries a blob URL that the delete cascade would orphan.
        var avatarUrl = app.BlobStorage.CreateOwnedBlobUrl("users", "avatar.png");
        var clubImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club.png");
        var versionImageUrl = app.BlobStorage.CreateOwnedBlobUrl("clubs", "club-v1.png");
        var eventImageUrl1 = app.BlobStorage.CreateOwnedBlobUrl("events", "event-1.png");
        var eventImageUrl2 = app.BlobStorage.CreateOwnedBlobUrl("events", "event-2.png");

        await app.QueryDbAsync(async db =>
        {
            var owner = await db.Users.FindAsync(user.Id);
            owner!.Avatar = avatarUrl;

            var club = new Club
            {
                Name = "Blob Club",
                Description = "Club with an image",
                Clubtype = ClubType.Social,
                ClubImage = clubImageUrl,
                UserId = user.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync();

            db.ClubVersions.Add(new ClubVersion
            {
                ClubId = club.Id,
                VersionNumber = 1,
                ClubImage = versionImageUrl,
                ActorUserId = user.Id
            });

            var ev = new EventEntity
            {
                Name = "Blob Event",
                ClubId = club.Id
            };
            db.Events.Add(ev);
            await db.SaveChangesAsync();

            db.EventImages.Add(new EventImage { EventId = ev.Id, ImageUrl = eventImageUrl1, SortOrder = 0 });
            db.EventImages.Add(new EventImage { EventId = ev.Id, ImageUrl = eventImageUrl2, SortOrder = 1 });
            await db.SaveChangesAsync();
            return true;
        });

        var session = await app.LoginApiAsync(
            "blob-delete",
            trustedDeviceToken: "blob-delete-device");
        await app.CompleteSessionMfaByEmailAsync("blob-delete@example.com", session.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/profile");
        await AddAuthAndCsrfAsync(app, request, session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.FindUserByEmailAsync("blob-delete@example.com")).Should().BeNull();

        // Every blob the deleted account (and its cascaded clubs/events) referenced is gone.
        app.BlobStorage.IsOwnedBlobUrl(avatarUrl).Should().BeFalse();
        app.BlobStorage.IsOwnedBlobUrl(clubImageUrl).Should().BeFalse();
        app.BlobStorage.IsOwnedBlobUrl(versionImageUrl).Should().BeFalse();
        app.BlobStorage.IsOwnedBlobUrl(eventImageUrl1).Should().BeFalse();
        app.BlobStorage.IsOwnedBlobUrl(eventImageUrl2).Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUsername_ShouldRequireMfa_AndRejectTakenUsername()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        // An existing account already owns the username.
        var existing = await app.SeedUserAsync("username-owner@example.com");
        await app.QueryDbAsync(async db =>
        {
            var user = await db.Users.FindAsync(existing.Id);
            user!.Username = "takenname";
            await db.SaveChangesAsync();
            return true;
        });

        // A different user, signed in, tries to claim the same username.
        var actor = await app.SeedUserAsync("username-other@example.com");
        await app.SeedKnownDeviceAsync(actor.Id, "other-device");
        var session = await app.LoginApiAsync(
            "username-other",
            trustedDeviceToken: "other-device");

        var conflict = new HttpRequestMessage(HttpMethod.Patch, "/api/profile/username")
        {
            Content = JsonContent.Create(new ChangeUsernameRequest { Username = "takenname" })
        };
        await AddAuthAndCsrfAsync(app, conflict, session.AccessToken);
        var gatedResponse = await app.Client.SendAsync(conflict);
        gatedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gatedResponse.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");

        await app.CompleteSessionMfaByEmailAsync(
            "username-other@example.com",
            session.AccessToken);
        conflict = new HttpRequestMessage(HttpMethod.Patch, "/api/profile/username")
        {
            Content = JsonContent.Create(new ChangeUsernameRequest { Username = "takenname" })
        };
        await AddAuthAndCsrfAsync(app, conflict, session.AccessToken);
        var response = await app.Client.SendAsync(conflict);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already taken");
        (await response.Content.ReadAsStringAsync()).Should().Contain("USERNAME_TAKEN");
    }

    // A real, decodable image: avatars are now decoded and re-encoded, so a bare signature is
    // no longer enough to be accepted. Generated here rather than checked in as a binary asset.
    private static readonly byte[] MinimalPng = EncodeImage(
        new Image<Rgba32>(4, 4, new Rgba32(40, 90, 160)),
        (image, stream) => image.SaveAsPng(stream));

    private static byte[] EncodeImage(Image<Rgba32> image, Action<Image<Rgba32>, Stream> save)
    {
        using (image)
        using (var stream = new MemoryStream())
        {
            save(image, stream);
            return stream.ToArray();
        }
    }

    private static async Task<HttpResponseMessage> PostAvatarAsync(
        AuthApiTestApp app,
        string accessToken,
        byte[] bytes,
        string contentType,
        string fileName)
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "image", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/profile/avatar")
        {
            Content = multipart
        };
        await AddAuthAndCsrfAsync(app, request, accessToken);
        return await app.Client.SendAsync(request);
    }

    private static async Task AddAuthAndCsrfAsync(
        AuthApiTestApp app,
        HttpRequestMessage request,
        string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
    }
}
