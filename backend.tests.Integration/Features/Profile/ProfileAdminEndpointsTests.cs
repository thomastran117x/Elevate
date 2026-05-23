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
        var adminSession = await app.LoginApiAsync("profile-admin@example.com", trustedDeviceToken: "profile-admin-device");

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
