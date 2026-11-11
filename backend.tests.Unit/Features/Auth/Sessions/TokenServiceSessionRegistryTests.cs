using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.features.auth.sessions;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.Sessions;

/// <summary>
/// How <see cref="TokenService"/> keeps the session registry authoritative over Redis.
/// </summary>
public class TokenServiceSessionRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GenerateRefreshToken_ShouldRecordANewSession_WithoutTokenMaterial()
    {
        await using var fixture = await Fixture.CreateAsync();

        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser("198.51.100.23"), SessionTransport.BrowserCookie, rememberMe: true);

        var stored = await fixture.Db.FindAsync(issue.SessionId);
        stored.Should().NotBeNull();
        stored!.UserId.Should().Be(fixture.UserId);
        stored.Transport.Should().Be(SessionTransport.BrowserCookie);
        stored.RememberMe.Should().BeTrue();
        stored.Client.Should().Be(SecurityHistoryClient.Chrome);
        stored.DeviceClass.Should().Be(AuthDeviceClass.Desktop);
        stored.Network.Should().Be("198.51.100.0/24");
        stored.CreatedAt.Should().Be(Start.UtcDateTime);
        stored.ExpiresAt.Should().Be(Start.UtcDateTime.AddDays(30));
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_ShouldMoveTheRecordedExpiryForward()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);

        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.RefreshAsync(issue);

        var stored = await fixture.Db.FindAsync(issue.SessionId);
        stored!.CreatedAt.Should().Be(Start.UtcDateTime);
        stored.LastSeenAt.Should().Be(Start.UtcDateTime.AddHours(6));
        stored.ExpiresAt.Should().Be(Start.UtcDateTime.AddHours(6).AddDays(1));
    }

    /// <summary>
    /// A session issued before the registry existed lives only in Redis; its next refresh is
    /// what records it.
    /// </summary>
    [Fact]
    public async Task Refresh_ShouldRecordASessionThatOnlyRedisKnew()
    {
        await using var fixture = await Fixture.CreateAsync();
        var beforeRegistry = new TokenService(fixture.Cache, fixture.Clock);
        var issue = await beforeRegistry.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        (await fixture.Db.FindAsync(issue.SessionId)).Should().BeNull();

        await fixture.RefreshAsync(issue);

        var stored = await fixture.Db.FindAsync(issue.SessionId);
        stored.Should().NotBeNull();
        stored!.UserId.Should().Be(fixture.UserId);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_ShouldBeRefused_AndWithdrawTheRedisSession_WhenTheRowWasRevoked()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        // Revoked in the registry alone, as when the Redis cleanup after a revocation never landed.
        await fixture.Db.Repository.RevokeAsync(issue.SessionId, AuthSecurityReason.Logout, Start.UtcDateTime);

        var act = () => fixture.RefreshAsync(issue);

        await act.Should().ThrowAsync<UnauthorizedException>();
        fixture.Cache.Keys.Should().NotContain($"refresh:v2:session:{issue.SessionId}");
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldWithdrawANewSession_ThatCouldNotBeRecorded()
    {
        var cache = new InMemoryCacheService();
        var registry = new Mock<IAuthSessionRepository>();
        registry.Setup(r => r.RecordUseAsync(It.IsAny<AuthSession>()))
            .ThrowsAsync(new NotAvailableException());
        var service = new TokenService(cache, sessions: registry.Object);

        var act = () => service.GenerateRefreshToken(7, 1, Browser(), SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<NotAvailableException>();
        cache.Keys.Should().NotContain(key => key.StartsWith("refresh:v2:session:"));
    }

    [Fact]
    public async Task RevokeRefreshSessionAsync_ShouldSucceed_WhenOnlyTheRegistryCouldBeUpdated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        fixture.Cache.ScriptsUnavailable = true;

        await fixture.Service.RevokeRefreshSessionAsync(issue.SessionId, AuthSecurityReason.Logout);

        var stored = await fixture.Db.FindAsync(issue.SessionId);
        stored!.RevokedAt.Should().Be(Start.UtcDateTime);
        stored.RevocationReason.Should().Be(AuthSecurityReason.Logout);
        var revoked = fixture.Events.Of(AuthSecurityEvents.SessionRevoked).Should().ContainSingle().Subject;
        revoked.UserId.Should().Be(fixture.UserId);
        revoked.SessionId.Should().Be(issue.SessionId);

        // The cache recovers with the session still in it; the row is what refuses it.
        fixture.Cache.ScriptsUnavailable = false;
        var act = () => fixture.RefreshAsync(issue);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task RevokeUserRefreshSessionAsync_ShouldRefuse_AnotherAccountsSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var otherId = await fixture.Db.SeedUserAsync("other");
        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);

        (await fixture.Service.RevokeUserRefreshSessionAsync(otherId, issue.SessionId, AuthSecurityReason.Logout))
            .Should().BeFalse();

        (await fixture.Db.FindAsync(issue.SessionId))!.RevokedAt.Should().BeNull();
        fixture.Cache.Keys.Should().Contain($"refresh:v2:session:{issue.SessionId}");
        fixture.Events.Of(AuthSecurityEvents.SessionRevoked).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeUserRefreshSessionAsync_ShouldEndTheSession_AndRecordTheRevocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issue = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);

        (await fixture.Service.RevokeUserRefreshSessionAsync(fixture.UserId, issue.SessionId, AuthSecurityReason.Logout))
            .Should().BeTrue();

        (await fixture.Db.FindAsync(issue.SessionId))!.RevokedAt.Should().NotBeNull();
        fixture.Cache.Keys.Should().NotContain($"refresh:v2:session:{issue.SessionId}");
        fixture.Events.Of(AuthSecurityEvents.SessionRevoked).Should().ContainSingle()
            .Which.SessionId.Should().Be(issue.SessionId);
    }

    [Fact]
    public async Task RevokeOtherRefreshSessionsAsync_ShouldKeepTheCurrentSession_AndEndSessionsWithoutARow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        var laptop = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        var legacy = await new TokenService(fixture.Cache, fixture.Clock).GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.ApiToken);

        var revoked = await fixture.Service.RevokeOtherRefreshSessionsAsync(
            fixture.UserId, current.SessionId, AuthSecurityReason.Logout);

        revoked.Should().Be(2);
        (await fixture.Db.FindAsync(current.SessionId))!.RevokedAt.Should().BeNull();
        (await fixture.Db.FindAsync(laptop.SessionId))!.RevokedAt.Should().NotBeNull();
        fixture.Cache.Keys.Should().Contain($"refresh:v2:session:{current.SessionId}");
        fixture.Cache.Keys.Should().NotContain($"refresh:v2:session:{laptop.SessionId}");
        fixture.Cache.Keys.Should().NotContain($"refresh:v2:session:{legacy.SessionId}");
        await fixture.RefreshAsync(current);

        var recorded = fixture.Events.Of(AuthSecurityEvents.SessionRevoked).Should().ContainSingle().Subject;
        recorded.SessionId.Should().BeNull();
        recorded.Outcome.Should().Be(AuthSecurityOutcome.Succeeded);
    }

    [Fact]
    public async Task RevokeOtherRefreshSessionsAsync_ShouldReportAnUnreachableCache_AfterRevokingTheRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var current = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        var laptop = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        fixture.Cache.ScriptsUnavailable = true;

        var act = () => fixture.Service.RevokeOtherRefreshSessionsAsync(
            fixture.UserId, current.SessionId, AuthSecurityReason.Logout);

        await act.Should().ThrowAsync<NotAvailableException>();
        (await fixture.Db.FindAsync(laptop.SessionId))!.RevokedAt.Should().NotBeNull();
        (await fixture.Db.FindAsync(current.SessionId))!.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllRefreshSessionsAsync_ShouldRevokeTheRows_AndCountItAsDone_ThroughACacheOutage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.BrowserCookie);
        var second = await fixture.Service.GenerateRefreshToken(
            fixture.UserId, 1, Browser(), SessionTransport.ApiToken);
        fixture.Cache.ScriptsUnavailable = true;

        await fixture.Service.RevokeAllRefreshSessionsAsync(fixture.UserId, AuthSecurityReason.PasswordChanged);

        (await fixture.Db.FindAsync(first.SessionId))!.RevokedAt.Should().NotBeNull();
        (await fixture.Db.FindAsync(second.SessionId))!.RevocationReason.Should().Be(AuthSecurityReason.PasswordChanged);
        fixture.Events.Of(AuthSecurityEvents.SessionRevoked).Should().ContainSingle()
            .Which.Outcome.Should().Be(AuthSecurityOutcome.Succeeded);
    }

    private static ClientRequestInfo Browser(string ipAddress = "203.0.113.5") => new()
    {
        IpAddress = ipAddress,
        ClientName = "Chrome",
        DeviceType = "Desktop",
        IsBrowserClient = true,
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public required AuthSessionDbHarness Db
        {
            get; init;
        }

        public required InMemoryCacheService Cache
        {
            get; init;
        }

        public required SteppingTimeProvider Clock
        {
            get; init;
        }

        public required RecordingAuthSecurityEventEmitter Events
        {
            get; init;
        }

        public required TokenService Service
        {
            get; init;
        }

        public required int UserId
        {
            get; init;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = await AuthSessionDbHarness.CreateAsync();
            var cache = new InMemoryCacheService();
            var clock = new SteppingTimeProvider(Start);
            var events = new RecordingAuthSecurityEventEmitter();
            return new Fixture
            {
                Db = db,
                Cache = cache,
                Clock = clock,
                Events = events,
                Service = new TokenService(cache, clock, events, db.Repository),
                UserId = await db.SeedUserAsync("owner"),
            };
        }

        /// <summary>Validates the token and issues the rotated one, as a refresh does.</summary>
        public async Task<RefreshTokenIssue> RefreshAsync(RefreshTokenIssue issue)
        {
            var validation = await Service.ValidateRefreshToken(
                issue.Value, issue.SessionBindingToken, issue.Transport, Browser());
            return await Service.GenerateRefreshToken(
                validation.UserId, 1, Browser(), issue.Transport, validation.SessionId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
