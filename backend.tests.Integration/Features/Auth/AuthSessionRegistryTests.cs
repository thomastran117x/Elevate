using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The PostgreSQL session registry is what decides whether a session's access and refresh tokens
/// are still accepted, whatever Redis still holds.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AuthSessionRegistryTests
{
    [Fact]
    public async Task SignIn_ShouldRecordTheSession_WithoutTokenMaterialOrTheRawAddress()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("registry-record@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "registry-record-device");

        var session = await app.LoginApiAsync("registry-record", trustedDeviceToken: "registry-record-device");

        var sessionId = ReadSessionId(session.AccessToken);
        var stored = await app.QueryDbAsync(db => db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));
        stored.UserId.Should().Be(user.Id);
        stored.Transport.Should().Be(SessionTransport.ApiToken);
        stored.RevokedAt.Should().BeNull();
        stored.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        // The test host talks over loopback, which is never shown.
        stored.Network.Should().BeNull();
    }

    [Fact]
    public async Task Logout_ShouldStopTheAccessToken_Immediately()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("registry-logout@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "registry-logout-device");
        var session = await app.LoginApiAsync("registry-logout", trustedDeviceToken: "registry-logout-device");
        (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await app.Client.PostAsJsonAsync("/api/auth/api/logout", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var sessionId = ReadSessionId(session.AccessToken);
        var stored = await app.QueryDbAsync(db => db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));
        stored.RevocationReason.Should().Be(AuthSecurityReason.Logout);
    }

    /// <summary>
    /// A session issued before the registry shipped is known only to Redis. Its access token is
    /// refused, and the refresh that follows records it, so the client carries on.
    /// </summary>
    [Fact]
    public async Task ASessionOnlyRedisKnew_ShouldBeRecordedByItsNextRefresh()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("registry-backfill@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "registry-backfill-device");
        var session = await app.LoginApiAsync("registry-backfill", trustedDeviceToken: "registry-backfill-device");
        var sessionId = ReadSessionId(session.AccessToken);
        await app.QueryDbAsync(db => db.AuthSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync());

        (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refreshed = await RefreshAsync(app, session);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(refreshed));
        var rotated = (await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(refreshed)).Data!;

        ReadSessionId(rotated.AccessToken).Should().Be(sessionId);
        (await app.GetWithBearerAsync("/api/auth/me", rotated.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.QueryDbAsync(db => db.AuthSessions.AnyAsync(s => s.Id == sessionId && s.RevokedAt == null)))
            .Should().BeTrue();
    }

    /// <summary>
    /// The revocation is written to the registry first; if the Redis cleanup after it never lands,
    /// the session that survives in Redis must still be refused.
    /// </summary>
    [Fact]
    public async Task ASessionRevokedOnlyInTheRegistry_ShouldBeRefusedForBothTokens()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("registry-revoked@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "registry-revoked-device");
        var session = await app.LoginApiAsync("registry-revoked", trustedDeviceToken: "registry-revoked-device");
        var sessionId = ReadSessionId(session.AccessToken);
        await app.QueryDbAsync(db => db.AuthSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, DateTime.UtcNow)));

        (await app.GetWithBearerAsync("/api/auth/me", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(app, session)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.Cache.GetValueAsync($"refresh:v2:session:{sessionId}")).Should().BeNull();
    }

    private static Task<HttpResponseMessage> RefreshAsync(AuthApiTestApp app, AuthenticatedSessionResponse session) =>
        app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });

    private static string ReadSessionId(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .First(claim => claim.Type == TokenService.SessionIdClaimType)
            .Value;
}
