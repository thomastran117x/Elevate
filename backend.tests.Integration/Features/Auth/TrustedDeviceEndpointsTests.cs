using System.Net;
using System.Net.Http.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.shared.utilities;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The owner's trusted-device list and forgetting devices, end to end.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TrustedDeviceEndpointsTests
{
    [Fact]
    public async Task List_ShouldShowOnlyTheCallersDevices_WithThePresentedOneMarked()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignInAsync(app, "devices-list");
        await app.SeedKnownDeviceAsync(userId, "devices-list-laptop", deviceType: "Mobile", clientName: "Firefox");
        var stranger = await app.SeedUserAsync("devices-list-stranger@example.com");
        await app.SeedKnownDeviceAsync(stranger.Id, "devices-list-stranger-device");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/devices");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var body = await response.Content.ReadAsStringAsync();
        var devices = (await app.ReadApiResponseAsync<TrustedDevicesResponse>(response)).Data!.Devices;
        devices.Should().HaveCount(2);
        // The sign-in left its device token in the client's cookie jar.
        devices.Should().ContainSingle(d => d.Current).Which.Client.Should().Be("chrome");
        devices.Should().ContainSingle(d => d.Client == "firefox" && d.DeviceClass == "mobile" && !d.Current);
        devices.Should().OnlyContain(d => d.ExpiresAt > DateTime.UtcNow.AddDays(89));
        body.Should().NotContain(CryptoHelper.HashToken("devices-list-device"))
            .And.NotContain("devices-list-device")
            .And.NotContain("127.0.0.1");
    }

    [Fact]
    public async Task Revocations_ShouldRequireAFreshStepUp_AndACsrfToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignInAsync(app, "devices-gate");
        var deviceId = await DeviceIdAsync(app, userId, "devices-gate-device");

        var gated = await ForgetAsync(app, session.AccessToken, deviceId);
        gated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gated.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
        (await ForgetAllAsync(app, session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await app.GrantStepUpProofAsync(session.AccessToken);
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/devices/{deviceId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        var withoutCsrf = await app.Client.SendAsync(request);
        withoutCsrf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withoutCsrf.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");

        (await app.QueryDbAsync(db => db.Devices.CountAsync(d => d.UserId == userId))).Should().Be(1);
    }

    [Fact]
    public async Task Forget_ShouldMakeTheNextSignInVerifyAgain_WithoutEndingSessions()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignInAsync(app, "devices-forget");
        var deviceId = await DeviceIdAsync(app, userId, "devices-forget-device");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var response = await ForgetAsync(app, session.AccessToken, deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var result = (await app.ReadApiResponseAsync<TrustedDeviceRevocationResponse>(response)).Data!;
        result.RevokedCount.Should().Be(1);
        result.CurrentDeviceRevoked.Should().BeTrue();
        response.Headers.GetValues("Set-Cookie")
            .Should().Contain(c => c.StartsWith($"{HttpUtility.TrustedDeviceCookieName}=;"));
        (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var signIn = await LoginAsync(app, "devices-forget", "devices-forget-device");
        signIn.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await signIn.Content.ReadAsStringAsync()).Should().Contain("Device verification required");
    }

    [Fact]
    public async Task Forget_ShouldBeNotFound_ForAnotherAccountsDevice_AndLeaveItAlone()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, _) = await SignInAsync(app, "devices-owner");
        var stranger = await app.SeedUserAsync("devices-stranger@example.com");
        await app.SeedKnownDeviceAsync(stranger.Id, "devices-stranger-device");
        var strangersDevice = await DeviceIdAsync(app, stranger.Id, "devices-stranger-device");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var response = await ForgetAsync(app, session.AccessToken, strangersDevice);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.QueryDbAsync(db => db.Devices.AnyAsync(d => d.Id == strangersDevice))).Should().BeTrue();
        (await ForgetAsync(app, session.AccessToken, int.MaxValue)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ForgetAll_ShouldRemoveEveryDeviceOfTheCaller_AndClearTheCookie()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignInAsync(app, "devices-all");
        await app.SeedKnownDeviceAsync(userId, "devices-all-phone", deviceType: "Mobile");
        var stranger = await app.SeedUserAsync("devices-all-stranger@example.com");
        await app.SeedKnownDeviceAsync(stranger.Id, "devices-all-stranger-device");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var response = await ForgetAllAsync(app, session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var result = (await app.ReadApiResponseAsync<TrustedDeviceRevocationResponse>(response)).Data!;
        result.RevokedCount.Should().Be(2);
        result.CurrentDeviceRevoked.Should().BeTrue();
        response.Headers.GetValues("Set-Cookie")
            .Should().Contain(c => c.StartsWith($"{HttpUtility.TrustedDeviceCookieName}=;"));
        (await app.QueryDbAsync(db => db.Devices.AnyAsync(d => d.UserId == userId))).Should().BeFalse();
        (await app.QueryDbAsync(db => db.Devices.AnyAsync(d => d.UserId == stranger.Id))).Should().BeTrue();
    }

    private static async Task<(AuthenticatedSessionResponse Session, int UserId)> SignInAsync(
        AuthApiTestApp app,
        string username)
    {
        var user = await app.SeedUserAsync($"{username}@example.com");
        await app.SeedKnownDeviceAsync(user.Id, $"{username}-device");
        var session = await app.LoginApiAsync(username, trustedDeviceToken: $"{username}-device");
        return (session, user.Id);
    }

    private static Task<int> DeviceIdAsync(AuthApiTestApp app, int userId, string deviceToken) =>
        app.QueryDbAsync(db => db.Devices
            .Where(d => d.UserId == userId && d.DeviceTokenHash == CryptoHelper.HashToken(deviceToken))
            .Select(d => d.Id)
            .SingleAsync());

    private static async Task<HttpResponseMessage> ForgetAsync(AuthApiTestApp app, string accessToken, int deviceId) =>
        await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/devices/{deviceId}"), accessToken);

    private static async Task<HttpResponseMessage> ForgetAllAsync(AuthApiTestApp app, string accessToken) =>
        await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, "/api/auth/devices"), accessToken);

    private static async Task<HttpResponseMessage> SendAsync(AuthApiTestApp app, HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> LoginAsync(AuthApiTestApp app, string username, string trustedDeviceToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Username = username,
                Password = AuthApiTestApp.DefaultPassword,
                Captcha = "captcha",
                Transport = SessionTransportResolver.ApiValue
            })
        };
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, trustedDeviceToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }
}
