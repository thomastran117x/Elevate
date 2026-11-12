using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The owner's session list and revocation, end to end: ownership, step-up, CSRF, both kinds of
/// token, browser cookies, and a Redis failure part way through.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SessionManagementEndpointsTests
{
    [Fact]
    public async Task List_ShouldShowOnlyTheCallersSessions_WithTheCurrentOneMarked()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-list");
        var stranger = await app.SignUpAndVerifyByTokenAsync(
            "sessions-list-stranger@example.com", transport: SessionTransportResolver.ApiValue);

        var response = await app.GetWithBearerAsync("/api/auth/sessions", second.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var body = await response.Content.ReadAsStringAsync();
        var sessions = (await app.ReadApiResponseAsync<AuthSessionsResponse>(response)).Data!.Sessions;
        sessions.Select(s => s.Id).Should().BeEquivalentTo([SessionIdOf(first), SessionIdOf(second)]);
        sessions.Should().ContainSingle(s => s.Current).Which.Id.Should().Be(SessionIdOf(second));
        sessions.Should().OnlyContain(s => s.Transport == "api" && s.Client == "chrome" && s.DeviceClass == "desktop");
        body.Should().NotContain(SessionIdOf(stranger.AccessToken));
        // Nothing that could be replayed or that identifies the network precisely.
        body.Should().NotContain(first.RefreshToken!).And.NotContain(first.SessionBindingToken!);
        body.Should().NotContainEquivalentOf("hash").And.NotContain("127.0.0.1");
    }

    [Fact]
    public async Task Revocations_ShouldRequireAFreshStepUp()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-gate");

        await AssertMfaRequiredAsync(await RevokeAsync(app, second.AccessToken, SessionIdOf(first)));
        await AssertMfaRequiredAsync(await RevokeOthersAsync(app, second.AccessToken));
        await AssertMfaRequiredAsync(await RevokeAllAsync(app, second.AccessToken));

        (await app.GetWithBearerAsync("/api/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revocations_ShouldRequireACsrfToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-csrf");
        await app.GrantStepUpProofAsync(second.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{SessionIdOf(first)}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", second.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");
    }

    [Fact]
    public async Task Revoke_ShouldStopTheOtherSessionsAccessAndRefreshTokens_AndKeepTheCallers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-revoke");
        await app.GrantStepUpProofAsync(second.AccessToken);

        var response = await RevokeAsync(app, second.AccessToken, SessionIdOf(first));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var result = (await app.ReadApiResponseAsync<SessionRevocationResponse>(response)).Data!;
        result.CurrentSessionRevoked.Should().BeFalse();
        result.RevokedCount.Should().Be(1);
        CookiesOf(response).Should().NotContain(c => c.StartsWith($"{HttpUtility.RefreshCookieName}=;"));

        (await app.GetWithBearerAsync("/api/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(app, first)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.GetWithBearerAsync("/api/auth/me", second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await StoredSessionAsync(app, SessionIdOf(first));
        stored.RevocationReason.Should().Be(AuthSecurityReason.UserRevokedSession);

        // Already ended: now indistinguishable from an id that never existed.
        (await RevokeAsync(app, second.AccessToken, SessionIdOf(first))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revoke_ShouldBeNotFound_ForAnotherAccountsSession_AndLeaveItAlone()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (_, owner) = await SignInTwiceAsync(app, "sessions-owner");
        var stranger = await app.SignUpAndVerifyByTokenAsync(
            "sessions-stranger@example.com", transport: SessionTransportResolver.ApiValue);
        await app.GrantStepUpProofAsync(owner.AccessToken);

        var response = await RevokeAsync(app, owner.AccessToken, SessionIdOf(stranger.AccessToken));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(SessionIdOf(stranger.AccessToken));
        (await app.GetWithBearerAsync("/api/auth/me", stranger.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RevokeAsync(app, owner.AccessToken, "00000000000000000000000000000000"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeOthers_ShouldKeepTheCurrentSessionAndItsStepUpProof()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-others");
        await app.GrantStepUpProofAsync(second.AccessToken);

        var response = await RevokeOthersAsync(app, second.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var result = (await app.ReadApiResponseAsync<SessionRevocationResponse>(response)).Data!;
        result.RevokedCount.Should().Be(1);
        result.CurrentSessionRevoked.Should().BeFalse();
        (await app.GetWithBearerAsync("/api/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(app, first)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", second.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefreshAsync(app, second)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StoredSessionAsync(app, SessionIdOf(first))).RevocationReason
            .Should().Be(AuthSecurityReason.UserRevokedOtherSessions);
    }

    [Fact]
    public async Task RevokingTheCurrentSession_ShouldEndIt_AndClearTheBrowserCookies()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-self");
        await app.GrantStepUpProofAsync(second.AccessToken);

        var response = await RevokeAsync(app, second.AccessToken, SessionIdOf(second));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        (await app.ReadApiResponseAsync<SessionRevocationResponse>(response)).Data!.CurrentSessionRevoked.Should().BeTrue();
        CookiesOf(response).Should().Contain(c => c.StartsWith($"{HttpUtility.RefreshCookieName}=;"))
            .And.Contain(c => c.StartsWith($"{HttpUtility.RefreshBindingCookieName}=;"));
        (await app.GetWithBearerAsync("/api/auth/me", second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.GetWithBearerAsync("/api/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeAll_ShouldEndEverySession_AndClearTheBrowserCookies()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (first, second) = await SignInTwiceAsync(app, "sessions-all");
        await app.GrantStepUpProofAsync(second.AccessToken);

        var response = await RevokeAllAsync(app, second.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var result = (await app.ReadApiResponseAsync<SessionRevocationResponse>(response)).Data!;
        result.RevokedCount.Should().Be(2);
        result.CurrentSessionRevoked.Should().BeTrue();
        CookiesOf(response).Should().Contain(c => c.StartsWith($"{HttpUtility.RefreshCookieName}=;"));
        foreach (var session in new[] { first, second })
        {
            (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await RefreshAsync(app, session)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    /// <summary>
    /// The row is revoked before Redis is touched. If Redis then fails, the request still succeeds
    /// and, once Redis is back with the session still in it, neither token is accepted.
    /// </summary>
    [Fact]
    public async Task Revoke_ShouldHold_WhenRedisFailsBeforeTheSessionIsRemovedFromIt()
    {
        var outage = new CacheScriptOutage();
        await using var app = await AuthApiTestApp.CreateAsync(serviceOverrides: outage.Install);
        var (first, second) = await SignInTwiceAsync(app, "sessions-outage");
        await app.GrantStepUpProofAsync(second.AccessToken);
        outage.Failing = script => script.Contains("redis.call('DEL', KEYS[1], KEYS[2])", StringComparison.Ordinal);

        var response = await RevokeAsync(app, second.AccessToken, SessionIdOf(first));
        outage.Failing = null;

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        (await app.Cache.GetValueAsync($"refresh:v2:session:{SessionIdOf(first)}")).Should().NotBeNull();
        (await app.GetWithBearerAsync("/api/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(app, first)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Two sessions of one account: a verified signup, then a sign-in on a known device.</summary>
    private static async Task<(AuthenticatedSessionResponse First, AuthenticatedSessionResponse Second)> SignInTwiceAsync(
        AuthApiTestApp app,
        string username)
    {
        var first = await app.SignUpAndVerifyByTokenAsync($"{username}@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync($"{username}@example.com");
        await app.SeedKnownDeviceAsync(user!.Id, $"{username}-device");
        var second = await app.LoginApiAsync(username, trustedDeviceToken: $"{username}-device");
        return (first, second);
    }

    private static async Task<HttpResponseMessage> RevokeAsync(AuthApiTestApp app, string accessToken, string sessionId) =>
        await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/sessions/{sessionId}"), accessToken);

    private static async Task<HttpResponseMessage> RevokeOthersAsync(AuthApiTestApp app, string accessToken) =>
        await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sessions/others"), accessToken);

    private static async Task<HttpResponseMessage> RevokeAllAsync(AuthApiTestApp app, string accessToken) =>
        await SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, "/api/auth/sessions"), accessToken);

    private static async Task<HttpResponseMessage> SendAsync(AuthApiTestApp app, HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> RefreshAsync(AuthApiTestApp app, AuthenticatedSessionResponse session) =>
        app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });

    private static Task<backend.main.features.auth.sessions.AuthSession> StoredSessionAsync(AuthApiTestApp app, string sessionId) =>
        app.QueryDbAsync(db => db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

    private static async Task AssertMfaRequiredAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
    }

    private static IEnumerable<string> CookiesOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [];

    private static string SessionIdOf(AuthenticatedSessionResponse session) => SessionIdOf(session.AccessToken);

    private static string SessionIdOf(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .First(claim => claim.Type == TokenService.SessionIdClaimType)
            .Value;
}
