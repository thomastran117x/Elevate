using System.Net;
using System.Net.Http.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.device;
using backend.main.features.auth.token;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Trust ends 90 days after the verification that granted it, enforced by the server whatever
/// the client does with its cookie.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class TrustedDeviceExpiryTests
{
    [Fact]
    public async Task SignIn_ShouldAskForVerificationAgain_OnceTheTrustHasExpired_AndClearTheStaleCookie()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("device-expired@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "device-expired-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var login = await LoginAsync(app, "device-expired", "device-expired-token");

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.Content.ReadAsStringAsync()).Should().Contain("Device verification required");
        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(cookie =>
            cookie.StartsWith($"{HttpUtility.TrustedDeviceCookieName}=;") && cookie.Contains("expires=Thu, 01 Jan 1970"));
        (await app.QueryDbAsync(db => db.Devices.AnyAsync(d => d.UserId == user.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyingTheDeviceAgain_ShouldTrustItForAnotherNinetyDays()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("device-renew@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "device-renew-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));
        (await LoginAsync(app, "device-renew", "device-renew-token")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deviceEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.NewDevice && message.Email == "device-renew@example.com");

        var verify = await app.Client.PostAsJsonAsync("/api/auth/device/verify", new VerificationTokenRequest
        {
            Token = deviceEmail.Token,
            Transport = SessionTransportResolver.ApiValue
        });

        verify.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(verify));
        var trusted = await app.QueryDbAsync(db => db.Devices.AsNoTracking().SingleAsync(d => d.UserId == user.Id));
        trusted.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow + Device.TrustLifetime, TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Signing in on a trusted device sends the token back, but never moves the expiry forward.
    /// </summary>
    [Fact]
    public async Task SignIn_OnATrustedDevice_ShouldNotExtendTheTrust()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("device-fixed@example.com");
        var expiresAt = DateTime.UtcNow.AddDays(10);
        await app.SeedKnownDeviceAsync(user.Id, "device-fixed-token", expiresAt: expiresAt);

        var login = await LoginAsync(app, "device-fixed", "device-fixed-token");

        login.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(login));
        var stored = await app.QueryDbAsync(db => db.Devices.AsNoTracking()
            .SingleAsync(d => d.DeviceTokenHash == CryptoHelper.HashToken("device-fixed-token")));
        stored.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(1));
        login.Headers.GetValues("Set-Cookie").Should().Contain(cookie =>
            cookie.StartsWith($"{HttpUtility.TrustedDeviceCookieName}=device-fixed-token")
            && !cookie.Contains($"max-age={(int)Device.TrustLifetime.TotalSeconds}"));
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
