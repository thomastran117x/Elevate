using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.history;
using backend.main.features.auth.token;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The producer half of the account security history: real auth journeys through the API, read
/// back from the security-history topic exactly as the worker would receive them.
/// </summary>
public sealed class SecurityHistoryPipelineTests
{
    private const string ClientAddress = "203.0.113.50";

    [Fact]
    public async Task Login_ShouldPublishOnlyTheSuccess_WithoutCredentialsOrContactDetails()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-login@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-login-device");
        await app.MarkNotificationBoundaryAsync();

        var failed = await LoginAsync(app, "pipeline-login", "wrong passphrase for pipeline", "pipeline-login-device");
        failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var session = await app.LoginApiAsync("pipeline-login", trustedDeviceToken: "pipeline-login-device");

        var published = await app.ReadNewSecurityHistoryJsonAsync(TimeSpan.FromSeconds(10));

        // The failed attempt is deliberately not part of the owner's history.
        var raw = published.Should().ContainSingle().Subject.GetRawText();
        foreach (var secret in new[]
                 {
                     AuthApiTestApp.DefaultPassword,
                     "wrong passphrase for pipeline",
                     "pipeline-login@example.com",
                     "pipeline-login\"",
                     "pipeline-login-device",
                     session.AccessToken,
                     session.RefreshToken!,
                     session.SessionBindingToken!,
                     ClientAddress,
                 })
        {
            raw.Should().NotContain(secret);
        }

        var message = JsonSerializer.Deserialize<SecurityHistoryMessage>(raw, JsonOptions.Default)!;
        message.UserId.Should().Be(user.Id);
        message.Kind.Should().Be(AccountSecurityEventKind.SignedIn);
        message.EventName.Should().Be("auth.sign_in.succeeded");
        message.Method.Should().Be("password");
        message.Network.Should().Be("203.0.113.0/24");
        message.CorrelationId.Should().NotBeNullOrWhiteSpace();
        message.InitiatedByAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_ShouldPublishASignedOutEntry()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-logout@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-logout-device");
        var session = await app.LoginApiAsync("pipeline-logout", trustedDeviceToken: "pipeline-logout-device");

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/api/logout",
            new RefreshTokenRequest
            {
                RefreshToken = session.RefreshToken,
                SessionBindingToken = session.SessionBindingToken
            },
            session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));

        var message = await app.WaitForSecurityHistoryAsync(m =>
            m.UserId == user.Id && m.Kind == AccountSecurityEventKind.SignedOut);
        message.Reason.Should().Be("logout");
        message.EventName.Should().Be("auth.session.revoked");
    }

    private static Task<AuthApiTestApp> CreateAppAsync(Action<IServiceCollection>? serviceOverrides = null) =>
        AuthApiTestApp.CreateAsync(serviceOverrides: services =>
        {
            services.AddSingleton<IStartupFilter>(new ClientAddressStartupFilter(IPAddress.Parse(ClientAddress)));
            serviceOverrides?.Invoke(services);
        });

    private static async Task<HttpResponseMessage> LoginAsync(
        AuthApiTestApp app,
        string username,
        string password,
        string trustedDeviceToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Username = username,
                Password = password,
                Captcha = "captcha",
                Transport = SessionTransportResolver.ApiValue,
            })
        };
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, trustedDeviceToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private sealed class ClientAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
