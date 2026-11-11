using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.features.auth.sessions;
using backend.main.shared.requests;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth.Sessions;

public class AuthSessionRepositoryTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecordUseAsync_ShouldCreateTheRow_WhenTheSessionIsNotRecordedYet()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");

        var recorded = await harness.Repository.RecordUseAsync(
            AuthSessionDbHarness.NewSession(userId, "s1", Now, Now.AddDays(1)));

        recorded.Should().BeTrue();
        var stored = await harness.FindAsync("s1");
        stored!.UserId.Should().Be(userId);
        stored.ExpiresAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public async Task RecordUseAsync_ShouldMoveLastSeenAndExpiry_ButKeepCreatedAt()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");
        await harness.SeedSessionAsync(userId, "s1", Now, Now.AddDays(1));

        var later = Now.AddHours(5);
        var use = AuthSessionDbHarness.NewSession(userId, "s1", later, later.AddDays(30));
        use.CreatedAt = later;
        use.RememberMe = true;
        use.Client = SecurityHistoryClient.Firefox;
        use.Network = "203.0.113.0/24";
        var recorded = await harness.Repository.RecordUseAsync(use);

        recorded.Should().BeTrue();
        var stored = await harness.FindAsync("s1");
        stored!.CreatedAt.Should().Be(Now);
        stored.LastSeenAt.Should().Be(later);
        stored.ExpiresAt.Should().Be(later.AddDays(30));
        stored.RememberMe.Should().BeTrue();
        stored.Client.Should().Be(SecurityHistoryClient.Firefox);
        stored.Network.Should().Be("203.0.113.0/24");
    }

    /// <summary>
    /// A refresh validated just before the revocation must not bring the session back.
    /// </summary>
    [Fact]
    public async Task RecordUseAsync_ShouldRefuse_ARevokedSession()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");
        await harness.SeedSessionAsync(userId, "s1", Now, Now.AddDays(1), revokedAt: Now);

        var recorded = await harness.Repository.RecordUseAsync(
            AuthSessionDbHarness.NewSession(userId, "s1", Now.AddMinutes(1), Now.AddDays(2)));

        recorded.Should().BeFalse();
        (await harness.FindAsync("s1"))!.RevokedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RecordUseAsync_ShouldRefuse_AnExpiredSession()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");
        await harness.SeedSessionAsync(userId, "s1", Now.AddDays(-2), Now.AddDays(-1));

        var recorded = await harness.Repository.RecordUseAsync(
            AuthSessionDbHarness.NewSession(userId, "s1", Now, Now.AddDays(1)));

        recorded.Should().BeFalse();
    }

    [Fact]
    public async Task RecordUseAsync_ShouldRefuse_AnotherAccountsSessionId()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync("owner");
        var otherId = await harness.SeedUserAsync("other");
        await harness.SeedSessionAsync(ownerId, "s1", Now, Now.AddDays(1));

        var recorded = await harness.Repository.RecordUseAsync(
            AuthSessionDbHarness.NewSession(otherId, "s1", Now, Now.AddDays(1)));

        recorded.Should().BeFalse();
        (await harness.FindAsync("s1"))!.UserId.Should().Be(ownerId);
    }

    [Fact]
    public async Task GetAccessTokenStateAsync_ShouldReportTheAccountAndWhetherTheSessionIsLive()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner", authVersion: 3, disabled: true);
        await harness.SeedSessionAsync(userId, "live", Now, Now.AddDays(1));
        await harness.SeedSessionAsync(userId, "revoked", Now, Now.AddDays(1), revokedAt: Now);
        await harness.SeedSessionAsync(userId, "expired", Now.AddDays(-2), Now.AddMinutes(-1));

        (await harness.Repository.GetAccessTokenStateAsync(userId, "live", Now))
            .Should().Be(new AccessTokenSessionState(IsDisabled: true, AuthVersion: 3, SessionActive: true));
        (await harness.Repository.GetAccessTokenStateAsync(userId, "revoked", Now))!.SessionActive.Should().BeFalse();
        (await harness.Repository.GetAccessTokenStateAsync(userId, "expired", Now))!.SessionActive.Should().BeFalse();
        (await harness.Repository.GetAccessTokenStateAsync(userId, "unknown", Now))!.SessionActive.Should().BeFalse();
        (await harness.Repository.GetAccessTokenStateAsync(9999, "live", Now)).Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenStateAsync_ShouldNotAcceptAnotherAccountsSession()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync("owner");
        var otherId = await harness.SeedUserAsync("other");
        await harness.SeedSessionAsync(ownerId, "s1", Now, Now.AddDays(1));

        (await harness.Repository.GetAccessTokenStateAsync(otherId, "s1", Now))!.SessionActive.Should().BeFalse();
    }

    [Fact]
    public async Task ListActiveAsync_ShouldReturnOnlyTheAccountsLiveSessions_MostRecentFirst()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync("owner");
        var otherId = await harness.SeedUserAsync("other");
        await harness.SeedSessionAsync(ownerId, "older", Now.AddHours(-3), Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "newer", Now.AddHours(-1), Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "revoked", Now, Now.AddDays(1), revokedAt: Now);
        await harness.SeedSessionAsync(ownerId, "expired", Now.AddDays(-2), Now.AddMinutes(-1));
        await harness.SeedSessionAsync(otherId, "theirs", Now, Now.AddDays(1));

        var sessions = await harness.Repository.ListActiveAsync(ownerId, Now);

        sessions.Select(s => s.Id).Should().Equal("newer", "older");
    }

    [Fact]
    public async Task RevokeAsync_ShouldRevokeOnce_AndReportTheOwner()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");
        await harness.SeedSessionAsync(userId, "s1", Now, Now.AddDays(1));

        (await harness.Repository.RevokeAsync("s1", AuthSecurityReason.Logout, Now)).Should().Be(userId);
        (await harness.Repository.RevokeAsync("s1", AuthSecurityReason.Logout, Now.AddMinutes(1))).Should().BeNull();
        (await harness.Repository.RevokeAsync("unknown", AuthSecurityReason.Logout, Now)).Should().BeNull();

        var stored = await harness.FindAsync("s1");
        stored!.RevokedAt.Should().Be(Now);
        stored.RevocationReason.Should().Be(AuthSecurityReason.Logout);
    }

    [Fact]
    public async Task RevokeForUserAsync_ShouldOnlyRevokeTheAccountsOwnLiveSession()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync("owner");
        var otherId = await harness.SeedUserAsync("other");
        await harness.SeedSessionAsync(ownerId, "s1", Now, Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "expired", Now.AddDays(-2), Now.AddMinutes(-1));

        (await harness.Repository.RevokeForUserAsync(otherId, "s1", AuthSecurityReason.Logout, Now))
            .Should().BeFalse();
        (await harness.FindAsync("s1"))!.RevokedAt.Should().BeNull();

        (await harness.Repository.RevokeForUserAsync(ownerId, "expired", AuthSecurityReason.Logout, Now))
            .Should().BeFalse();
        (await harness.Repository.RevokeForUserAsync(ownerId, "s1", AuthSecurityReason.Logout, Now))
            .Should().BeTrue();
        (await harness.FindAsync("s1"))!.RevocationReason.Should().Be(AuthSecurityReason.Logout);
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldRevokeEveryLiveSession_ExceptTheOneKept()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync("owner");
        var otherId = await harness.SeedUserAsync("other");
        await harness.SeedSessionAsync(ownerId, "current", Now, Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "laptop", Now, Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "phone", Now, Now.AddDays(1));
        await harness.SeedSessionAsync(ownerId, "gone", Now, Now.AddDays(1), revokedAt: Now.AddHours(-1));
        await harness.SeedSessionAsync(otherId, "theirs", Now, Now.AddDays(1));

        var revoked = await harness.Repository.RevokeAllAsync(
            ownerId, AuthSecurityReason.PasswordChanged, Now, exceptSessionId: "current");

        revoked.Should().BeEquivalentTo(["laptop", "phone"]);
        (await harness.FindAsync("current"))!.RevokedAt.Should().BeNull();
        (await harness.FindAsync("theirs"))!.RevokedAt.Should().BeNull();
        (await harness.FindAsync("gone"))!.RevokedAt.Should().Be(Now.AddHours(-1));

        (await harness.Repository.RevokeAllAsync(ownerId, AuthSecurityReason.AccountDisabled, Now))
            .Should().Equal("current");
    }

    [Fact]
    public async Task DeleteExpiredAsync_ShouldDeleteOnlyRowsPastTheCutoff_InBatches()
    {
        await using var harness = await AuthSessionDbHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner");
        await harness.SeedSessionAsync(userId, "old-1", Now.AddDays(-5), Now.AddDays(-4));
        await harness.SeedSessionAsync(userId, "old-2", Now.AddDays(-5), Now.AddDays(-3), revokedAt: Now.AddDays(-5));
        await harness.SeedSessionAsync(userId, "revoked-live", Now, Now.AddDays(1), revokedAt: Now);
        await harness.SeedSessionAsync(userId, "live", Now, Now.AddDays(1));

        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 1)).Should().Be(1);
        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 10)).Should().Be(1);
        (await harness.Repository.DeleteExpiredAsync(Now, batchSize: 10)).Should().Be(0);

        (await harness.Db.AuthSessions.AsNoTracking().Select(s => s.Id).ToListAsync())
            .Should().BeEquivalentTo(["revoked-live", "live"]);
    }

    [Fact]
    public void DescribeClient_ShouldKeepOnlyTheClosedClassificationAndMaskedNetwork()
    {
        var session = AuthSessionDbHarness.NewSession(1, "s1", Now, Now.AddDays(1));

        session.DescribeClient(new ClientRequestInfo
        {
            ClientName = "EvilBot <script>",
            DeviceType = "Mobile",
            IpAddress = "203.0.113.77",
        });

        session.Client.Should().Be(SecurityHistoryClient.Other);
        session.DeviceClass.Should().Be(AuthDeviceClass.Mobile);
        session.Network.Should().Be("203.0.113.0/24");
    }

    [Theory]
    [InlineData("Unknown", "Unknown", "Unknown")]
    [InlineData("Chrome", "Desktop", "not-an-address")]
    public void DescribeClient_ShouldLeaveUnknownsUnknown(string clientName, string deviceType, string ipAddress)
    {
        var session = AuthSessionDbHarness.NewSession(1, "s1", Now, Now.AddDays(1));

        session.DescribeClient(new ClientRequestInfo
        {
            ClientName = clientName,
            DeviceType = deviceType,
            IpAddress = ipAddress,
        });

        session.Network.Should().BeNull();
        if (clientName == "Unknown")
        {
            session.Client.Should().Be(SecurityHistoryClient.Unknown);
            session.DeviceClass.Should().Be(AuthDeviceClass.Unknown);
        }
        else
        {
            session.Client.Should().Be(SecurityHistoryClient.Chrome);
            session.DeviceClass.Should().Be(AuthDeviceClass.Desktop);
        }
    }
}
