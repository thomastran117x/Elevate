using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.events;
using backend.main.features.auth.oauth;
using backend.main.features.auth.oauth.links;
using backend.main.features.auth.passkeys;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.AspNetCore.WebUtilities;

using Moq;

namespace backend.tests.Unit.Features.Auth.ProviderLinks;

public class ProviderLinkServiceTests
{
    private const int UserId = 7;
    private const string SessionId = "session-7";
    private const int AuthVersion = 3;
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ProviderLinkSession Session = new(UserId, SessionId, AuthVersion);

    private readonly Mock<IUserIdentityLinkRepository> _links = new();
    private readonly Mock<IAuthUserRepository> _users = new();
    private readonly Mock<IPasskeyCredentialRepository> _passkeys = NoPasskeys();
    private readonly Mock<IProviderLinkStateStore> _states = new();
    private readonly Mock<IRefreshAheadCache> _refreshCache = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IProviderLinkFlow> _google = Flow(IdentityProviders.Google, browserRedeemsCode: false);
    private readonly Mock<IProviderLinkFlow> _microsoft = Flow(IdentityProviders.Microsoft, browserRedeemsCode: true);
    private readonly RecordingAuthSecurityEventEmitter _events = new();

    public ProviderLinkServiceTests()
    {
        _links.Setup(r => r.ListForUserAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _links.Setup(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdentityLinkAddResult.Added);
        _users.Setup(r => r.GetAuthByIdAsync(UserId)).ReturnsAsync(new UserAuthRecord
        {
            Id = UserId,
            Email = "owner@example.com",
            Usertype = "participant",
            Password = "hash",
            AuthVersion = AuthVersion,
        });
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldListEveryLinkableProvider_WithItsLinkAndUnlinkRule()
    {
        _links.Setup(r => r.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([Link(IdentityProviders.Google, "google-1")]);
        _users.Setup(r => r.GetAuthByIdAsync(UserId)).ReturnsAsync(PasswordlessUser());
        _microsoft.Setup(f => f.IsAvailable).Returns(false);

        var overview = await CreateService().GetOverviewAsync(UserId);

        overview.HasPassword.Should().BeFalse();
        overview.Providers.Select(p => p.Provider).Should().Equal(IdentityProviders.Google, IdentityProviders.Microsoft);
        var google = overview.Providers[0];
        google.Link!.Subject.Should().Be("google-1");
        google.Available.Should().BeTrue();
        // The only way to sign in, so it cannot be removed.
        google.CanUnlink.Should().BeFalse();
        var microsoft = overview.Providers[1];
        microsoft.Link.Should().BeNull();
        microsoft.Available.Should().BeFalse();
        microsoft.CanUnlink.Should().BeFalse();
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldAllowUnlinkingTheOnlyLink_WhenAPasskeyRemains()
    {
        _links.Setup(r => r.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([Link(IdentityProviders.Google, "google-1")]);
        _users.Setup(r => r.GetAuthByIdAsync(UserId)).ReturnsAsync(PasswordlessUser());
        _passkeys.Setup(p => p.GetFactorSummaryAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyFactorSummary(1, Now));

        var overview = await CreateService().GetOverviewAsync(UserId);

        overview.Providers[0].CanUnlink.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, 1, 0, true)]
    [InlineData(false, 2, 0, true)]
    [InlineData(false, 1, 0, false)]
    [InlineData(false, 1, 1, true)]
    [InlineData(false, 0, 2, true)]
    [InlineData(false, 0, 1, false)]
    [InlineData(true, 0, 1, true)]
    public void SignInMethods_ShouldRemainOnlyWithAPasswordOrAnotherLinkOrPasskey(
        bool hasPassword, int linkCount, int passkeyCount, bool expected)
    {
        SignInMethods.RemainAfterRemoving(hasPassword, linkCount, passkeyCount).Should().Be(expected);
    }

    [Fact]
    public async Task StartAsync_ShouldBindTheStateToTheSession_AndKeepTheVerifierForGoogle()
    {
        ProviderLinkState? stored = null;
        _states.Setup(s => s.CreateLinkStateAsync(It.IsAny<ProviderLinkState>()))
            .Callback<ProviderLinkState>(state => stored = state)
            .ReturnsAsync("state-token");

        var start = await CreateService().StartAsync(Session, "Google");

        start.State.Should().Be("state-token");
        start.CodeVerifier.Should().BeNull();
        start.RedirectUri.Should().Be("https://app.example/auth/google");
        stored!.UserId.Should().Be(UserId);
        stored.SessionId.Should().Be(SessionId);
        stored.AuthVersion.Should().Be(AuthVersion);
        stored.Provider.Should().Be(IdentityProviders.Google);
        stored.RedirectUri.Should().Be("https://app.example/auth/google");
        stored.Nonce.Should().NotBeNullOrWhiteSpace();
        stored.CodeVerifier.Should().NotBe(stored.Nonce);
        _google.Verify(f => f.BuildAuthorizationUrl(
            "state-token",
            stored.Nonce,
            It.Is<string>(challenge => challenge == ExpectedChallenge(stored.CodeVerifier))), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldHandTheVerifierToTheBrowser_OnlyForMicrosoft()
    {
        ProviderLinkState? stored = null;
        _states.Setup(s => s.CreateLinkStateAsync(It.IsAny<ProviderLinkState>()))
            .Callback<ProviderLinkState>(state => stored = state)
            .ReturnsAsync("state-token");

        var start = await CreateService().StartAsync(Session, IdentityProviders.Microsoft);

        start.CodeVerifier.Should().Be(stored!.CodeVerifier);
        // The browser redeems with exactly the redirect the authorization request named.
        start.RedirectUri.Should().Be(stored.RedirectUri);
    }

    [Fact]
    public async Task StartAsync_ShouldRefuse_WhenTheAccountAlreadyLinksTheProvider()
    {
        _links.Setup(r => r.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([Link(IdentityProviders.Google, "google-1")]);

        var act = () => CreateService().StartAsync(Session, IdentityProviders.Google);

        (await act.Should().ThrowAsync<ProviderLinkConflictException>())
            .Which.ErrorCode.Should().Be(ProviderLinkConflictException.AlreadyConnectedCode);
        _states.Verify(s => s.CreateLinkStateAsync(It.IsAny<ProviderLinkState>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ShouldRefuse_AnUnsupportedOrUnconfiguredProvider()
    {
        _microsoft.Setup(f => f.IsAvailable).Returns(false);
        var service = CreateService();

        await service.Invoking(s => s.StartAsync(Session, IdentityProviders.Apple))
            .Should().ThrowAsync<ResourceNotFoundException>();
        await service.Invoking(s => s.StartAsync(Session, IdentityProviders.Microsoft))
            .Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task CompleteAsync_ShouldLinkTheVerifiedIdentity_AndRevokeOutstandingState()
    {
        GivenState(State());
        _google.Setup(f => f.ResolveIdentityAsync(It.IsAny<ProviderLinkState>(), "auth-code", null))
            .ReturnsAsync(new OAuthUser("google-new", "Other@Example.com", "Other Name", IdentityProviders.Google, true));

        var link = await CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "auth-code", null);

        link.Subject.Should().Be("google-new");
        link.Email.Should().Be("Other@Example.com");
        link.LinkedAtUtc.Should().Be(Now);
        _links.Verify(r => r.AddAsync(It.Is<UserIdentityLink>(l =>
            l.UserId == UserId && l.Provider == IdentityProviders.Google && l.Subject == "google-new"), It.IsAny<CancellationToken>()), Times.Once);
        _states.Verify(s => s.RevokeAllForUserAsync(UserId), Times.Once);
        _refreshCache.Verify(c => c.RemoveAsync(UserCacheKeys.For(UserId)), Times.Once);
        var linked = _events.Of(AuthSecurityEvents.ProviderLinked).Should().ContainSingle().Subject;
        linked.UserId.Should().Be(UserId);
        linked.Method.Should().Be(AuthSecurityMethod.Google);
        linked.Flow.Should().Be(AuthSecurityFlow.ProviderLink);
    }

    /// <summary>Providers do not bound their claims; an oversized name must not fail the link.</summary>
    [Fact]
    public async Task CompleteAsync_ShouldCutOversizedMetadataToTheColumns()
    {
        GivenState(State());
        _google.Setup(f => f.ResolveIdentityAsync(It.IsAny<ProviderLinkState>(), "code", null))
            .ReturnsAsync(new OAuthUser("google-long", "  a@b.com  ", new string('n', 400), IdentityProviders.Google, true));

        var link = await CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        link.DisplayName.Should().HaveLength(UserIdentityLink.DisplayNameMaxLength);
        link.Email.Should().Be("a@b.com");
    }

    [Fact]
    public async Task CompleteAsync_ShouldRefuse_AnExpiredOrAlreadyUsedState()
    {
        _states.Setup(s => s.ConsumeLinkStateAsync("state-token")).ReturnsAsync((ProviderLinkState?)null);

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        await act.Should().ThrowAsync<ProviderLinkExpiredException>();
        _google.Verify(f => f.ResolveIdentityAsync(It.IsAny<ProviderLinkState>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_ShouldRefuse_AStateOlderThanTheProofWindow()
    {
        GivenState(State(createdAtUtc: Now - ProviderLinkStateLifetime.Lifetime - TimeSpan.FromSeconds(1)));

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        await act.Should().ThrowAsync<ProviderLinkExpiredException>();
    }

    public static TheoryData<string, ProviderLinkState> MismatchedStates => new()
    {
        { "another account", State(userId: UserId + 1) },
        { "another session", State(sessionId: "session-other") },
        { "a changed AuthVersion", State(authVersion: AuthVersion + 1) },
        { "another provider", State(provider: IdentityProviders.Microsoft) },
    };

    [Theory]
    [MemberData(nameof(MismatchedStates))]
    public async Task CompleteAsync_ShouldRefuse_AStateBoundToADifferentSession(string because, ProviderLinkState state)
    {
        GivenState(state);

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        await act.Should().ThrowAsync<ProviderLinkMismatchException>(because);
        _links.Verify(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A refused token is a bad request, never a 401 the client would treat as sign-out.</summary>
    [Fact]
    public async Task CompleteAsync_ShouldReportARefusedProviderToken_AsABadRequest()
    {
        GivenState(State());
        _google.Setup(f => f.ResolveIdentityAsync(It.IsAny<ProviderLinkState>(), "code", null))
            .ThrowsAsync(new UnauthorizedException("Google nonce validation failed"));

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        await act.Should().ThrowAsync<BadRequestException>();
        _links.Verify(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A provider subject maps to at most one account, whatever its email says.</summary>
    [Fact]
    public async Task CompleteAsync_ShouldRefuse_AnIdentityLinkedToAnotherAccount()
    {
        GivenState(State());
        GivenIdentity("google-taken");
        _links.Setup(r => r.FindBySubjectAsync(IdentityProviders.Google, "google-taken", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(IdentityProviders.Google, "google-taken", userId: 99));

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        (await act.Should().ThrowAsync<ProviderLinkConflictException>())
            .Which.ErrorCode.Should().Be(ProviderLinkConflictException.AlreadyLinkedCode);
        _links.Verify(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
        var conflict = _events.Of(AuthSecurityEvents.ProviderLinkConflict).Should().ContainSingle().Subject;
        conflict.Reason.Should().Be(AuthSecurityReason.ProviderBoundToOtherAccount);
        conflict.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CompleteAsync_ShouldBeIdempotent_WhenTheIdentityIsAlreadyThisAccounts()
    {
        GivenState(State());
        GivenIdentity("google-mine");
        var existing = Link(IdentityProviders.Google, "google-mine");
        _links.Setup(r => r.FindBySubjectAsync(IdentityProviders.Google, "google-mine", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var link = await CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        link.Should().BeSameAs(existing);
        _links.Verify(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
        _events.Of(AuthSecurityEvents.ProviderLinked).Should().BeEmpty();
    }

    /// <summary>A concurrent link of the same identity by another account wins the unique index.</summary>
    [Fact]
    public async Task CompleteAsync_ShouldRefuse_WhenAConcurrentLinkOfTheSubjectWins()
    {
        GivenState(State());
        GivenIdentity("google-raced");
        _links.SetupSequence(r => r.FindBySubjectAsync(IdentityProviders.Google, "google-raced", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityLink?)null)
            .ReturnsAsync(Link(IdentityProviders.Google, "google-raced", userId: 99));
        _links.Setup(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdentityLinkAddResult.SubjectTaken);

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        (await act.Should().ThrowAsync<ProviderLinkConflictException>())
            .Which.ErrorCode.Should().Be(ProviderLinkConflictException.AlreadyLinkedCode);
        _states.Verify(s => s.RevokeAllForUserAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_ShouldRefuse_WhenAConcurrentLinkFilledTheProvider()
    {
        GivenState(State());
        GivenIdentity("google-second");
        _links.Setup(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdentityLinkAddResult.ProviderTaken);

        var act = () => CreateService().CompleteAsync(Session, IdentityProviders.Google, "state-token", "code", null);

        (await act.Should().ThrowAsync<ProviderLinkConflictException>())
            .Which.ErrorCode.Should().Be(ProviderLinkConflictException.AlreadyConnectedCode);
        _events.Of(AuthSecurityEvents.ProviderLinkConflict).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.ProviderAlreadyConnected);
    }

    [Fact]
    public async Task ConfirmEmailLinkAsync_ShouldLinkTheProposedIdentity_ForTheAccountItMatched()
    {
        _states.Setup(s => s.ConsumePendingEmailLinkAsync("link-token")).ReturnsAsync(Pending());

        var link = await CreateService().ConfirmEmailLinkAsync(UserId, "link-token");

        link.Subject.Should().Be("ms-proposed");
        link.Provider.Should().Be(IdentityProviders.Microsoft);
        _events.Of(AuthSecurityEvents.ProviderLinked).Should().ContainSingle()
            .Which.Flow.Should().Be(AuthSecurityFlow.ProviderLinkConfirmation);
        _states.Verify(s => s.RevokeAllForUserAsync(UserId), Times.Once);
    }

    [Fact]
    public async Task ConfirmEmailLinkAsync_ShouldRefuse_ConfirmationFromAnotherAccount()
    {
        _states.Setup(s => s.ConsumePendingEmailLinkAsync("link-token")).ReturnsAsync(Pending(userId: 99));

        var act = () => CreateService().ConfirmEmailLinkAsync(UserId, "link-token");

        await act.Should().ThrowAsync<ProviderLinkMismatchException>();
        _links.Verify(r => r.AddAsync(It.IsAny<UserIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmEmailLinkAsync_ShouldRefuse_AnExpiredProposal()
    {
        _states.Setup(s => s.ConsumePendingEmailLinkAsync("link-token"))
            .ReturnsAsync(Pending(createdAtUtc: Now - TimeSpan.FromMinutes(11)));

        var act = () => CreateService().ConfirmEmailLinkAsync(UserId, "link-token");

        await act.Should().ThrowAsync<ProviderLinkExpiredException>();
    }

    [Fact]
    public async Task UnlinkAsync_ShouldRemoveTheLink_AndRecordIt()
    {
        _links.Setup(r => r.RemoveIfAnotherMethodRemainsAsync(UserId, IdentityProviders.Microsoft, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityLinkRemoveResult.Removed);

        await CreateService().UnlinkAsync(UserId, "microsoft");

        _states.Verify(s => s.RevokeAllForUserAsync(UserId), Times.Once);
        _refreshCache.Verify(c => c.RemoveAsync(UserCacheKeys.For(UserId)), Times.Once);
        var unlinked = _events.Of(AuthSecurityEvents.ProviderUnlinked).Should().ContainSingle().Subject;
        unlinked.Method.Should().Be(AuthSecurityMethod.Microsoft);
        unlinked.Flow.Should().Be(AuthSecurityFlow.ProviderUnlink);
        // Nothing the removed provider signed in keeps access: every session ends.
        _tokens.Verify(t => t.RevokeAllRefreshSessionsAsync(UserId, AuthSecurityReason.ProviderUnlinked, null), Times.Once);
    }

    [Fact]
    public async Task UnlinkAsync_ShouldRefuse_TheLastSignInMethod()
    {
        _links.Setup(r => r.RemoveIfAnotherMethodRemainsAsync(UserId, IdentityProviders.Google, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityLinkRemoveResult.LastSignInMethod);

        var act = () => CreateService().UnlinkAsync(UserId, IdentityProviders.Google);

        (await act.Should().ThrowAsync<ProviderLinkConflictException>())
            .Which.ErrorCode.Should().Be(ProviderLinkConflictException.LastSignInMethodCode);
        _events.Of(AuthSecurityEvents.ProviderUnlinked).Should().BeEmpty();
        _states.Verify(s => s.RevokeAllForUserAsync(It.IsAny<int>()), Times.Never);
        _tokens.Verify(
            t => t.RevokeAllRefreshSessionsAsync(It.IsAny<int>(), It.IsAny<AuthSecurityReason>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task UnlinkAsync_ShouldReportAProviderThatIsNotLinked_AsNotFound()
    {
        _links.Setup(r => r.RemoveIfAnotherMethodRemainsAsync(UserId, IdentityProviders.Google, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityLinkRemoveResult.NotLinked);
        var service = CreateService();

        await service.Invoking(s => s.UnlinkAsync(UserId, IdentityProviders.Google))
            .Should().ThrowAsync<ResourceNotFoundException>();
        await service.Invoking(s => s.UnlinkAsync(UserId, "facebook"))
            .Should().ThrowAsync<ResourceNotFoundException>();
    }

    /// <summary>The change has committed; failing to revoke state or a cache entry must not undo it.</summary>
    [Fact]
    public async Task UnlinkAsync_ShouldSucceed_WhenRevocationFails()
    {
        _links.Setup(r => r.RemoveIfAnotherMethodRemainsAsync(UserId, IdentityProviders.Google, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityLinkRemoveResult.Removed);
        _states.Setup(s => s.RevokeAllForUserAsync(UserId)).ThrowsAsync(new InvalidOperationException("cache down"));
        _refreshCache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("cache down"));

        await CreateService().UnlinkAsync(UserId, IdentityProviders.Google);

        _events.Of(AuthSecurityEvents.ProviderUnlinked).Should().ContainSingle();
    }

    private ProviderLinkService CreateService() => new(
        _links.Object,
        _users.Object,
        _passkeys.Object,
        _states.Object,
        [_google.Object, _microsoft.Object],
        _refreshCache.Object,
        _events,
        _tokens.Object,
        new FixedTimeProvider(Now));

    private static Mock<IPasskeyCredentialRepository> NoPasskeys()
    {
        var passkeys = new Mock<IPasskeyCredentialRepository>();
        passkeys.Setup(p => p.GetFactorSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyFactorSummary(0, null));
        return passkeys;
    }

    private void GivenState(ProviderLinkState state) =>
        _states.Setup(s => s.ConsumeLinkStateAsync("state-token")).ReturnsAsync(state);

    private void GivenIdentity(string subject) =>
        _google.Setup(f => f.ResolveIdentityAsync(It.IsAny<ProviderLinkState>(), "code", null))
            .ReturnsAsync(new OAuthUser(subject, "owner@example.com", "Owner", IdentityProviders.Google, true));

    private static Mock<IProviderLinkFlow> Flow(string provider, bool browserRedeemsCode)
    {
        var flow = new Mock<IProviderLinkFlow>();
        flow.Setup(f => f.Provider).Returns(provider);
        flow.Setup(f => f.IsAvailable).Returns(true);
        flow.Setup(f => f.BrowserRedeemsCode).Returns(browserRedeemsCode);
        flow.Setup(f => f.RedirectUri).Returns($"https://app.example/auth/{provider}");
        flow.Setup(f => f.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns($"https://provider.example/{provider}/authorize");
        return flow;
    }

    private static ProviderLinkState State(
        int userId = UserId,
        string sessionId = SessionId,
        int authVersion = AuthVersion,
        string provider = IdentityProviders.Google,
        DateTime? createdAtUtc = null) => new()
        {
            Provider = provider,
            UserId = userId,
            SessionId = sessionId,
            AuthVersion = authVersion,
            Nonce = "nonce",
            CodeVerifier = "verifier",
            RedirectUri = "https://app.example/auth/google",
            CreatedAtUtc = createdAtUtc ?? Now,
        };

    private static PendingEmailLink Pending(int userId = UserId, DateTime? createdAtUtc = null) => new()
    {
        UserId = userId,
        Provider = IdentityProviders.Microsoft,
        Subject = "ms-proposed",
        Email = "owner@example.com",
        Name = "Owner",
        CreatedAtUtc = createdAtUtc ?? Now,
    };

    private static UserIdentityLink Link(string provider, string subject, int userId = UserId) => new()
    {
        Id = subject.GetHashCode(),
        UserId = userId,
        Provider = provider,
        Subject = subject,
        LinkedAtUtc = Now.AddDays(-1),
    };

    private static UserAuthRecord PasswordlessUser() => new()
    {
        Id = UserId,
        Email = "owner@example.com",
        Usertype = "participant",
        Password = null,
        AuthVersion = AuthVersion,
    };

    private static string ExpectedChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
