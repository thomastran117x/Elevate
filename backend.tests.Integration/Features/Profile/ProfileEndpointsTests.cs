using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using backend.main.application.security;
using backend.main.features.profile.contracts.requests;
using backend.main.features.profile.contracts.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

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
            "profile-user@example.com",
            trustedDeviceToken: "profile-user-device");

        // GET /api/profile
        var getResponse = await app.GetWithBearerAsync("/api/profile", session.AccessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await app.ReadApiResponseAsync<MyProfileResponse>(getResponse);
        initial.Data!.Email.Should().Be("profile-user@example.com");
        initial.Data.Phone.Should().BeNull();
        var expectedRole = initial.Data.Usertype;

        // PATCH /api/profile
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, "/api/profile")
        {
            Content = JsonContent.Create(new UpdateProfileRequest
            {
                Name = "Profile User",
                Username = "profileuser",
                Phone = "+1 416 555 0100",
                Address = "1 Test Street"
            })
        };
        await AddAuthAndCsrfAsync(app, patchRequest, session.AccessToken);
        var patchResponse = await app.Client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(patchResponse);
        updated.Data!.Name.Should().Be("Profile User");
        updated.Data.Username.Should().Be("profileuser");
        updated.Data.Phone.Should().Be("+1 416 555 0100");
        updated.Data.Address.Should().Be("1 Test Street");
        // Identity and role must be preserved (never mutated via a profile update).
        updated.Data.Email.Should().Be("profile-user@example.com");
        updated.Data.Usertype.Should().Be(expectedRole);

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
    }

    [Fact]
    public async Task UploadAvatar_ShouldStoreAvatarUrl()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("avatar-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "avatar-device");
        var session = await app.LoginApiAsync(
            "avatar-user@example.com",
            trustedDeviceToken: "avatar-device");

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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await app.ReadApiResponseAsync<MyProfileResponse>(response);
        updated.Data!.Avatar.Should().NotBeNullOrWhiteSpace();
        updated.Data.Avatar!.Should().Contain("avatar.png");
    }

    [Fact]
    public async Task ChangePassword_ShouldSucceed_AndAllowLoginWithNewPassword()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("pw-user@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pw-device");
        var session = await app.LoginApiAsync(
            "pw-user@example.com",
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
            "pw-user@example.com",
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
            "delete-user@example.com",
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
    public async Task UpdateProfile_WithUsernameTakenByAnother_ShouldReturnConflict()
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
            "username-other@example.com",
            trustedDeviceToken: "other-device");

        var conflict = new HttpRequestMessage(HttpMethod.Patch, "/api/profile")
        {
            Content = JsonContent.Create(new UpdateProfileRequest { Username = "takenname" })
        };
        await AddAuthAndCsrfAsync(app, conflict, session.AccessToken);
        var response = await app.Client.SendAsync(conflict);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("already taken");
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
