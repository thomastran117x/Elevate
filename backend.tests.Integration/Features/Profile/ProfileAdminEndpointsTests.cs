using System.Net;
using System.Net.Http.Json;

using backend.main.features.profile;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Features.Profile;

public class ProfileAdminEndpointsTests
{
    [Fact]
    public async Task AdminStatusEndpoint_ShouldDisableAndReEnableUser()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var admin = await app.SeedUserAsync("profile-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "profile-admin-device");
        var adminSession = await app.LoginApiAsync("profile-admin", trustedDeviceToken: "profile-admin-device");
        await app.GrantStepUpProofAsync(adminSession.AccessToken);

        var target = await app.SeedUserAsync("profile-target@example.com", role: "Participant");

        var disable = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/admin/users/{target.Id}/status",
            adminSession.AccessToken,
            JsonContent.Create(new
            {
                isDisabled = true,
                reason = "policy"
            })));
        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await disable.Content.ReadAsStringAsync()).Should().Contain("User disabled successfully.");

        var disabledUser = await app.FindUserByEmailAsync("profile-target@example.com");
        disabledUser.Should().NotBeNull();
        disabledUser!.IsDisabled.Should().BeTrue();
        disabledUser.DisabledReason.Should().Be("policy");

        var enable = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/admin/users/{target.Id}/status",
            adminSession.AccessToken,
            JsonContent.Create(new
            {
                isDisabled = false as bool?,
                reason = (string?)null
            })));
        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        (await enable.Content.ReadAsStringAsync()).Should().Contain("User re-enabled successfully.");

        var enabledUser = await app.FindUserByEmailAsync("profile-target@example.com");
        enabledUser.Should().NotBeNull();
        enabledUser!.IsDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task AdminStatusEndpoint_ShouldRequireFreshStepUp()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var admin = await app.SeedUserAsync("profile-stepup-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "profile-stepup-admin-device");
        var adminSession = await app.LoginApiAsync("profile-stepup-admin", trustedDeviceToken: "profile-stepup-admin-device");

        var target = await app.SeedUserAsync("profile-stepup-target@example.com", role: "Participant");

        var withoutProof = await SendDisableAsync(app, target.Id, adminSession.AccessToken);
        await AssertMfaRequiredAsync(withoutProof);

        // A proof verified 10 minutes and a second ago is past its window.
        await app.GrantStepUpProofAsync(adminSession.AccessToken, DateTime.UtcNow.AddMinutes(-10).AddSeconds(-1));
        var staleProof = await SendDisableAsync(app, target.Id, adminSession.AccessToken);
        await AssertMfaRequiredAsync(staleProof);

        var unchanged = await app.FindUserByEmailAsync("profile-stepup-target@example.com");
        unchanged!.IsDisabled.Should().BeFalse();

        await app.GrantStepUpProofAsync(adminSession.AccessToken);
        var withFreshProof = await SendDisableAsync(app, target.Id, adminSession.AccessToken);
        withFreshProof.StatusCode.Should().Be(HttpStatusCode.OK, await withFreshProof.Content.ReadAsStringAsync());

        var disabled = await app.FindUserByEmailAsync("profile-stepup-target@example.com");
        disabled!.IsDisabled.Should().BeTrue();
    }

    private static Task<HttpResponseMessage> SendDisableAsync(AuthApiTestApp app, int userId, string accessToken) =>
        app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Patch,
            $"/api/admin/users/{userId}/status",
            accessToken,
            JsonContent.Create(new
            {
                isDisabled = true,
                reason = "policy"
            })));

    private static async Task AssertMfaRequiredAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return request;
    }
}
