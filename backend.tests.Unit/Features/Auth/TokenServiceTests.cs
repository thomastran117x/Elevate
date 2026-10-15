using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using backend.main.features.auth.mfa.session;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class TokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_ShouldIncludeIdentityRoleAndAuthVersionClaims()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User
        {
            Id = 23,
            Email = "claims@example.com",
            Usertype = "participant",
            AuthVersion = 7
        };

        var issue = service.GenerateAccessToken(user, "session-abc");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issue.Value);

        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.NameIdentifier || claim.Type == "nameid") && claim.Value == "23");
        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.Name || claim.Type == "unique_name") && claim.Value == "claims@example.com");
        jwt.Claims.Should().Contain(claim => (claim.Type == ClaimTypes.Role || claim.Type == "role") && claim.Value == "Participant");
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.AuthVersionClaimType && claim.Value == "7");
        jwt.Claims.Should().Contain(claim => claim.Type == TokenService.SessionIdClaimType && claim.Value == "session-abc");
        issue.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(10));
    }

    // ------------------------------------------------------- email change

    [Fact]
    public async Task GenerateEmailChangeArtifactsAsync_ShouldBindTheArtifactsToTheAccount()
    {
        var service = new TokenService(new InMemoryCacheService());

        var artifacts = await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");
        var pending = await service.ConsumeEmailChangeTokenAsync(artifacts.LinkToken);

        pending.UserId.Should().Be(23);
        pending.AuthVersion.Should().Be(1);
        pending.NewEmail.Should().Be("new@example.com");
    }

    [Fact]
    public async Task ConsumeEmailChangeOtpAsync_ShouldReturnTheAccountAndAddress()
    {
        var service = new TokenService(new InMemoryCacheService());

        var artifacts = await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");
        var pending = await service.ConsumeEmailChangeOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge);

        pending.UserId.Should().Be(23);
        pending.AuthVersion.Should().Be(1);
        pending.NewEmail.Should().Be("new@example.com");
    }

    [Fact]
    public async Task ConsumeEmailChangeTokenAsync_ShouldRejectAReusedToken()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");

        await service.ConsumeEmailChangeTokenAsync(artifacts.LinkToken);
        var act = () => service.ConsumeEmailChangeTokenAsync(artifacts.LinkToken);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    /// <summary>
    /// A signup token must not be redeemable as an email change: its payload carries no account
    /// id, and treating one purpose as another is how a verification flow becomes a takeover.
    /// </summary>
    [Fact]
    public async Task ConsumeEmailChangeTokenAsync_ShouldRejectAnotherPurposesToken()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User { Email = "signup@example.com", Usertype = "participant" };
        var token = await service.GenerateVerificationToken(user, VerificationPurpose.SignUp);

        var act = () => service.ConsumeEmailChangeTokenAsync(token);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*purpose mismatch*");
    }

    [Fact]
    public async Task GetPendingEmailChangeAsync_ShouldReportTheAddressAwaitingConfirmation()
    {
        var service = new TokenService(new InMemoryCacheService());
        await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");

        var pending = await service.GetPendingEmailChangeAsync(23);

        pending.Should().NotBeNull();
        pending!.NewEmail.Should().Be("new@example.com");
    }

    [Fact]
    public async Task GetPendingEmailChangeAsync_ShouldReportNothing_AfterConfirmation()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");

        await service.ConsumeEmailChangeTokenAsync(artifacts.LinkToken);

        (await service.GetPendingEmailChangeAsync(23)).Should().BeNull();
    }

    [Fact]
    public async Task CancelPendingEmailChangeAsync_ShouldMakeTheLinkUnusable()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateEmailChangeArtifactsAsync(23, 1, "new@example.com");

        await service.CancelPendingEmailChangeAsync(23);

        (await service.GetPendingEmailChangeAsync(23)).Should().BeNull();
        var act = () => service.ConsumeEmailChangeTokenAsync(artifacts.LinkToken);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    /// <summary>
    /// State is keyed by the target address, so re-requesting against a different one would strand
    /// the first request's artifacts as separately redeemable without an explicit cancel.
    /// </summary>
    [Fact]
    public async Task GenerateEmailChangeArtifactsAsync_ShouldInvalidateAnEarlierRequest()
    {
        var service = new TokenService(new InMemoryCacheService());
        var first = await service.GenerateEmailChangeArtifactsAsync(23, 1, "first@example.com");

        await service.GenerateEmailChangeArtifactsAsync(23, 1, "second@example.com");

        var act = () => service.ConsumeEmailChangeTokenAsync(first.LinkToken);
        await act.Should().ThrowAsync<UnauthorizedException>();

        (await service.GetPendingEmailChangeAsync(23))!.NewEmail.Should().Be("second@example.com");
    }

    /// <summary>
    /// Two requests racing must not leave a redeemable proof that nothing can reach.
    /// </summary>
    /// <remarks>
    /// Each pending change is stored under its own target address, so without serialization both
    /// calls clear the same empty index, both mint a token, and only the later one stays indexed -
    /// leaving the earlier proof live but invisible to a later cancel. The lock makes the second
    /// request either wait its turn or be refused, never overlap.
    /// </remarks>
    [Fact]
    public async Task GenerateEmailChangeArtifactsAsync_ShouldNotLeaveAnUnreachableProof_WhenRequestsRace()
    {
        var service = new TokenService(new InMemoryCacheService());

        var first = await service.GenerateEmailChangeArtifactsAsync(23, 1, "first@example.com");
        var second = await service.GenerateEmailChangeArtifactsAsync(23, 1, "second@example.com");

        // Whatever the interleaving, cancelling has to reach every proof the account holds.
        await service.CancelPendingEmailChangeAsync(23);

        var firstAct = () => service.ConsumeEmailChangeTokenAsync(first.LinkToken);
        await firstAct.Should().ThrowAsync<UnauthorizedException>();

        var secondAct = () => service.ConsumeEmailChangeTokenAsync(second.LinkToken);
        await secondAct.Should().ThrowAsync<UnauthorizedException>();
    }

    /// <summary>
    /// The account id is appended to the OTP proof only when present, so proofs already issued for
    /// signup and reset hash identically across the deploy that added it. This pins that the two
    /// purposes without an id still verify.
    /// </summary>
    [Fact]
    public async Task VerifyVerificationOtpAsync_ShouldStillAcceptSignupProofs()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User
        {
            Email = "signup@example.com",
            Password = "hashed",
            Username = "ada",
            Usertype = "participant"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(
            user,
            VerificationPurpose.SignUp);
        var verified = await service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.SignUp);

        verified.Email.Should().Be("signup@example.com");
        verified.Username.Should().Be("ada");
    }

    [Fact]
    public async Task RevokeRefreshSessionAsync_ShouldDeleteTheSessionsStepUpProof()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.BrowserCookie);
        await cache.SetValueAsync(StepUpProofKeys.ForSession(issue.SessionId), "{}", TimeSpan.FromMinutes(10));

        await service.RevokeRefreshSessionAsync(issue.SessionId);

        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(issue.SessionId))).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshSessionAsync_ShouldDeleteStepUpProof_EvenAfterTheSessionExpired()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        await cache.SetValueAsync(StepUpProofKeys.ForSession("gone"), "{}", TimeSpan.FromMinutes(10));

        await service.RevokeRefreshSessionAsync("gone");

        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession("gone"))).Should().BeFalse();
    }

    [Fact]
    public async Task StoreStepUpProofAsync_ShouldStoreWithTheLifetime_WhileTheSessionIsLive()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.BrowserCookie);

        var result = await service.StoreStepUpProofAsync(issue.SessionId, 8, 0, "{\"proof\":1}", TimeSpan.FromMinutes(10));

        result.Should().Be(StepUpProofWriteResult.Stored);
        (await cache.GetValueAsync(StepUpProofKeys.ForSession(issue.SessionId))).Should().Be("{\"proof\":1}");
        (await cache.GetTTLAsync(StepUpProofKeys.ForSession(issue.SessionId)))!.Value
            .Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task StoreStepUpProofAsync_ShouldWriteNothing_AfterTheSessionWasRevoked()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.BrowserCookie);
        await service.RevokeRefreshSessionAsync(issue.SessionId);

        // A step-up verify that finishes after logout must not resurrect a proof.
        var result = await service.StoreStepUpProofAsync(issue.SessionId, 8, 0, "{}", TimeSpan.FromMinutes(10));

        result.Should().Be(StepUpProofWriteResult.SessionEnded);
        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(issue.SessionId))).Should().BeFalse();
    }

    [Fact]
    public async Task StoreStepUpProofAsync_ShouldReportUnavailable_WhenTheCacheIsDown()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .ReturnsAsync(backend.main.features.cache.CacheScriptResult.Unavailable);
        var service = new TokenService(cache.Object);

        var result = await service.StoreStepUpProofAsync("session", 8, 0, "{}", TimeSpan.FromMinutes(10));

        result.Should().Be(StepUpProofWriteResult.Unavailable);
    }

    [Fact]
    public async Task RevokeRefreshSessionAsync_ShouldDeleteTheSessionAndProofTogether()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync("{\"SessionId\":\"s1\",\"UserId\":8,\"CurrentRefreshTokenHash\":\"\"}");
        StackExchange.Redis.RedisKey[]? deletedTogether = null;
        cache.Setup(c => c.TryEvalAsync(
                It.Is<string>(script => script.Contains("redis.call('DEL'")),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .Callback<string, StackExchange.Redis.RedisKey[], StackExchange.Redis.RedisValue[]>((_, keys, _) => deletedTogether = keys)
            .ReturnsAsync(new backend.main.features.cache.CacheScriptResult(true, 1L));
        var service = new TokenService(cache.Object);

        await service.RevokeRefreshSessionAsync("s1");

        deletedTogether!.Select(key => key.ToString())
            .Should().Equal("refresh:v2:session:s1", StepUpProofKeys.ForSession("s1"));
        cache.Verify(c => c.DeleteKeyAsync("refresh:v2:session:s1"), Times.Never);
    }

    [Fact]
    public async Task RevokeRefreshSessionAsync_ShouldFail_WhenTheCacheCannotConfirmTheRevocation()
    {
        // Reporting a successful logout here would leave the proof usable for its full window.
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .ReturnsAsync(backend.main.features.cache.CacheScriptResult.Unavailable);
        var service = new TokenService(cache.Object);

        var act = () => service.RevokeRefreshSessionAsync("s1");

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task StoreStepUpProofAsync_ShouldRefuse_WhenTheGenerationMovedDuringVerification()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.BrowserCookie);
        await cache.IncrementAsync(StepUpProofKeys.GenerationForUser(8));

        // Verification began at generation 0; a factor change has since advanced it to 1.
        var result = await service.StoreStepUpProofAsync(issue.SessionId, 8, 0, "{}", TimeSpan.FromMinutes(10));

        result.Should().Be(StepUpProofWriteResult.Superseded);
        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(issue.SessionId))).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectTransportMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.ApiToken,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token transport mismatch.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectMissingBindingToken()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            null,
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Missing session binding token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectBindingTokenMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            "wrong-binding",
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid session binding token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectReuseAfterSuccessfulValidation()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);

        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        result.UserId.Should().Be(8);

        var act = () => service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token was already rotated.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRevokeTheSession_WhenARotatedTokenIsReplayedAfterTheGraceWindow()
    {
        var cache = new InMemoryCacheService();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(cache, clock);
        var (first, newest) = await RotateAsync(service);
        await cache.SetValueAsync(StepUpProofKeys.ForSession(newest.SessionId), "{}", TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromSeconds(11));

        var replay = () => ValidateAsync(service, first);

        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token reuse detected.");
        (await cache.KeyExistsAsync($"refresh:v2:session:{newest.SessionId}")).Should().BeFalse();
        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(newest.SessionId))).Should().BeFalse();

        // Whoever holds the newest token, attacker or user, has to sign in again.
        var next = () => ValidateAsync(service, newest);
        await next.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRevokeOnReplay_EvenWithoutTheBindingToken()
    {
        var cache = new InMemoryCacheService();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(cache, clock);
        var (first, newest) = await RotateAsync(service);
        clock.Advance(TimeSpan.FromMinutes(5));

        var replay = () => service.ValidateRefreshToken(
            first.Value,
            null,
            SessionTransport.ApiToken,
            CreateRequestInfo());

        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token reuse detected.");
        (await cache.KeyExistsAsync($"refresh:v2:session:{newest.SessionId}")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldKeepTheSession_WhenADuplicateArrivesWithinTheGraceWindow()
    {
        var cache = new InMemoryCacheService();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(cache, clock);
        var (first, newest) = await RotateAsync(service);
        clock.Advance(TimeSpan.FromSeconds(9));

        var duplicate = () => ValidateAsync(service, first);

        await duplicate.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token was already rotated.");
        var result = await ValidateAsync(service, newest);
        result.SessionId.Should().Be(newest.SessionId);
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldDeriveTheTokenFromTheSessionAndGeneration()
    {
        var service = new TokenService(new InMemoryCacheService());

        var (first, newest) = await RotateAsync(service);

        first.Value.Should().StartWith($"v1.{first.SessionId}.1.");
        newest.Value.Should().StartWith($"v1.{first.SessionId}.2.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldStoreNothingPerToken_AcrossRotations()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);

        for (var rotation = 0; rotation < 5; rotation++)
        {
            var validation = await ValidateAsync(service, issue);
            issue = await service.GenerateRefreshToken(
                8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);
        }

        cache.Keys.Should().NotContain(key => key.StartsWith("refresh:v2:token:"));
        cache.Keys.Should().Contain($"refresh:v2:session:{issue.SessionId}");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRevokeTheSession_WhenAnOlderGenerationIsReplayedInsideTheGraceWindow()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var (first, second) = await RotateAsync(service);
        var validation = await ValidateAsync(service, second);
        await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);

        // Only the generation just rotated away can be a harmless duplicate.
        var replay = () => ValidateAsync(service, first);

        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token reuse detected.");
        (await cache.KeyExistsAsync($"refresh:v2:session:{first.SessionId}")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectAForgedTokenWithoutRevoking()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        var forged = issue.Value[..issue.Value.LastIndexOf('.')] + ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        var act = () => service.ValidateRefreshToken(
            forged, issue.SessionBindingToken, SessionTransport.ApiToken, CreateRequestInfo());

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
        (await ValidateAsync(service, issue)).SessionId.Should().Be(issue.SessionId);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectAReplay_AfterTheSessionWasRevoked()
    {
        var cache = new InMemoryCacheService();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(cache, clock);
        var (first, newest) = await RotateAsync(service);
        await service.RevokeRefreshSessionAsync(newest.SessionId);
        clock.Advance(TimeSpan.FromMinutes(5));

        var replay = () => ValidateAsync(service, first);

        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRotateOnce_WhenTwoRefreshesRaceOnOneToken()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        RefreshTokenValidationResult? winner = null;
        cache.BeforeNextScript = async () => winner = await ValidateAsync(service, issue);

        var loser = () => ValidateAsync(service, issue);

        await loser.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token was already rotated.");
        winner.Should().NotBeNull();
        (await cache.KeyExistsAsync($"refresh:v2:session:{issue.SessionId}")).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldNotRecreateASessionRevokedDuringRotation()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        cache.BeforeNextScript = () => service.RevokeRefreshSessionAsync(issue.SessionId);

        var act = () => ValidateAsync(service, issue);

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await cache.KeyExistsAsync($"refresh:v2:session:{issue.SessionId}")).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldNotRecreateASessionRevokedBeforeTheNextTokenIsWritten()
    {
        // A reuse revocation landing between a refresh's validation and its write of the next
        // token must stay revoked: the refresh must not bring the session back.
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        var validation = await ValidateAsync(service, issue);
        cache.BeforeNextScript = () => service.RevokeRefreshSessionAsync(issue.SessionId);

        var act = () => service.GenerateRefreshToken(
            8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session is invalid or expired.");
        (await cache.KeyExistsAsync($"refresh:v2:session:{issue.SessionId}")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldFailClosed_WhenTheGenerationCannotBeAdvanced()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var issue = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        cache.ScriptsUnavailable = true;

        var act = () => ValidateAsync(service, issue);

        await act.Should().ThrowAsync<NotAvailableException>();
        cache.ScriptsUnavailable = false;
        (await ValidateAsync(service, issue)).SessionId.Should().Be(issue.SessionId);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldMoveALegacySessionOntoDerivedTokens()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var legacy = await SeedLegacySessionAsync(cache);

        var validation = await ValidateAsync(service, legacy);
        var next = await service.GenerateRefreshToken(
            8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);

        next.Value.Should().StartWith($"v1.{legacy.SessionId}.1.");
        (await ValidateAsync(service, next)).SessionId.Should().Be(legacy.SessionId);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRevokeTheSession_WhenARotatedLegacyTokenIsReplayed()
    {
        var cache = new InMemoryCacheService();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var service = new TokenService(cache, clock);
        var legacy = await SeedLegacySessionAsync(cache);
        var validation = await ValidateAsync(service, legacy);
        var next = await service.GenerateRefreshToken(
            8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);

        var duplicate = () => ValidateAsync(service, legacy);
        await duplicate.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token was already rotated.");

        clock.Advance(TimeSpan.FromSeconds(11));
        var replay = () => ValidateAsync(service, legacy);
        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token reuse detected.");

        var newest = () => ValidateAsync(service, next);
        await newest.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldFailClosed_WhenALegacyTokenCannotBeRetired()
    {
        var cache = CreateCacheWithLiveToken();
        cache.Setup(c => c.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .ReturnsAsync(backend.main.features.cache.CacheScriptResult.Unavailable);
        var service = new TokenService(cache.Object);

        var act = () => service.ValidateRefreshToken(
            LiveToken, "binding", SessionTransport.ApiToken, CreateRequestInfo());

        await act.Should().ThrowAsync<NotAvailableException>();
        cache.Verify(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldRejectWithoutRevoking_WhenAConcurrentRefreshRetiredALegacyTokenFirst()
    {
        var cache = CreateCacheWithLiveToken();
        cache.Setup(c => c.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .ReturnsAsync(new backend.main.features.cache.CacheScriptResult(true, 0L));
        var service = new TokenService(cache.Object);

        var act = () => service.ValidateRefreshToken(
            LiveToken, "binding", SessionTransport.ApiToken, CreateRequestInfo());

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh token was already rotated.");
        cache.Verify(c => c.TryEvalAsync(
            It.Is<string>(script => script.Contains("redis.call('DEL'")),
            It.IsAny<StackExchange.Redis.RedisKey[]>(),
            It.IsAny<StackExchange.Redis.RedisValue[]>()), Times.Never);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldFailClosed_WhenLegacyReuseCannotRevokeTheSession()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        var tokenKey = RefreshTokenKey("replayed");
        cache.Setup(c => c.GetValueAsync(tokenKey))
            .ReturnsAsync("{\"SessionId\":\"s1\",\"UserId\":8,\"RetiredAt\":\"2026-10-14T12:00:00Z\"}");
        cache.Setup(c => c.GetValueAsync("refresh:v2:session:s1"))
            .ReturnsAsync("{\"SessionId\":\"s1\",\"UserId\":8,\"CurrentRefreshTokenHash\":\"\",\"CurrentBindingTokenHash\":\"\"}");
        cache.Setup(c => c.TryEvalAsync(
                It.IsAny<string>(),
                It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<StackExchange.Redis.RedisValue[]>()))
            .ReturnsAsync(backend.main.features.cache.CacheScriptResult.Unavailable);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 5, 0, TimeSpan.Zero));
        var service = new TokenService(cache.Object, clock);

        var act = () => service.ValidateRefreshToken(
            "replayed", "binding", SessionTransport.ApiToken, CreateRequestInfo());

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task VerifyVerificationOtpAsync_ShouldInvalidateChallengeAfterMaximumAttempts()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "signup@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrongAttempt = () => service.VerifyVerificationOtpAsync(
                "000000",
                artifacts.OtpChallenge.Challenge,
                VerificationPurpose.SignUp);

            await wrongAttempt.Should().ThrowAsync<UnauthorizedException>();
        }

        var validAttempt = () => service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.SignUp);

        await validAttempt.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired verification challenge.");
        (await service.VerificationTokenExist(user.Email, VerificationPurpose.SignUp)).Should().BeNull();
    }

    [Fact]
    public async Task GenerateAndValidateRefreshToken_ShouldPreserveUserAndTransport()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(14, 1, requestInfo, SessionTransport.ApiToken);
        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.ApiToken,
            requestInfo);

        result.UserId.Should().Be(14);
        result.Transport.Should().Be(SessionTransport.ApiToken);
        result.SessionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldRejectUserMismatch_WhenReusingExistingSession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var act = () => service.GenerateRefreshToken(
            9,
            1,
            requestInfo,
            SessionTransport.BrowserCookie,
            validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session user mismatch.");
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldRejectTransportMismatch_WhenReusingExistingSession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 1, requestInfo, SessionTransport.BrowserCookie);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var act = () => service.GenerateRefreshToken(
            8,
            1,
            requestInfo,
            SessionTransport.ApiToken,
            validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session transport mismatch.");
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldReturnIssuingAuthVersion()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 4, requestInfo, SessionTransport.BrowserCookie);
        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        result.AuthVersion.Should().Be(4);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldReturnNullAuthVersion_ForLegacySession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 4, requestInfo, SessionTransport.BrowserCookie);
        await RemoveSessionAuthVersionAsync(cache, issue.SessionId);

        var result = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        result.AuthVersion.Should().BeNull();
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldRejectAuthVersionMismatch_WhenReusingExistingSession()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 4, requestInfo, SessionTransport.BrowserCookie);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var act = () => service.GenerateRefreshToken(
            8,
            5,
            requestInfo,
            SessionTransport.BrowserCookie,
            validation.SessionId);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Refresh session is no longer valid.");
    }

    [Fact]
    public async Task GenerateRefreshToken_ShouldStampLegacySession_WhenRotating()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var requestInfo = CreateRequestInfo();

        var issue = await service.GenerateRefreshToken(8, 4, requestInfo, SessionTransport.BrowserCookie);
        await RemoveSessionAuthVersionAsync(cache, issue.SessionId);
        var validation = await service.ValidateRefreshToken(
            issue.Value,
            issue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        var rotated = await service.GenerateRefreshToken(
            8,
            6,
            requestInfo,
            SessionTransport.BrowserCookie,
            validation.SessionId);
        var result = await service.ValidateRefreshToken(
            rotated.Value,
            rotated.SessionBindingToken,
            SessionTransport.BrowserCookie,
            requestInfo);

        result.SessionId.Should().Be(issue.SessionId);
        result.AuthVersion.Should().Be(6);
    }

    private static async Task RemoveSessionAuthVersionAsync(InMemoryCacheService cache, string sessionId)
    {
        var key = $"refresh:v2:session:{sessionId}";
        var session = Newtonsoft.Json.Linq.JObject.Parse((await cache.GetValueAsync(key))!);
        session.Remove("AuthVersion");
        await cache.SetValueAsync(key, session.ToString(), TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task GenerateVerificationArtifactsAsync_ShouldReuseExistingState_ForSameEmailAndPurpose()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "signup@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var first = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);
        var second = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        second.LinkToken.Should().Be(first.LinkToken);
        second.OtpChallenge.Challenge.Should().Be(first.OtpChallenge.Challenge);
        second.OtpChallenge.Code.Should().Be(first.OtpChallenge.Code);
        second.Purpose.Should().Be(VerificationPurpose.SignUp);
    }

    [Fact]
    public async Task GenerateVerificationArtifactsAsync_ShouldRotateExistingResetState_WhenRequested()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User
        {
            Email = "reset@example.com",
            Usertype = "placeholder"
        };

        var first = await service.GenerateVerificationArtifactsAsync(
            user,
            VerificationPurpose.ResetPassword
        );
        var second = await service.GenerateVerificationArtifactsAsync(
            user,
            VerificationPurpose.ResetPassword,
            replaceExisting: true
        );

        second.LinkToken.Should().NotBe(first.LinkToken);
        second.OtpChallenge.Challenge.Should().NotBe(first.OtpChallenge.Challenge);
        var oldToken = () => service.VerifyVerificationToken(
            first.LinkToken,
            VerificationPurpose.ResetPassword
        );
        await oldToken.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task VerificationArtifacts_ShouldPreserveSignupUsername_ForLinkAndOtp()
    {
        var service = new TokenService(new InMemoryCacheService());
        var linkUser = new User
        {
            Email = "link@example.com",
            Username = "link-user",
            Password = "hashed-password",
            Usertype = "Organizer"
        };
        var otpUser = new User
        {
            Email = "otp@example.com",
            Username = "otp-user",
            Password = "hashed-password",
            Usertype = "Participant"
        };

        var linkArtifacts = await service.GenerateVerificationArtifactsAsync(
            linkUser,
            VerificationPurpose.SignUp
        );
        var otpArtifacts = await service.GenerateVerificationArtifactsAsync(
            otpUser,
            VerificationPurpose.SignUp
        );

        var verifiedByLink = await service.VerifyVerificationToken(
            linkArtifacts.LinkToken,
            VerificationPurpose.SignUp
        );
        var verifiedByOtp = await service.VerifyVerificationOtpAsync(
            otpArtifacts.OtpChallenge.Code,
            otpArtifacts.OtpChallenge.Challenge,
            VerificationPurpose.SignUp
        );

        verifiedByLink.Username.Should().Be("link-user");
        verifiedByOtp.Username.Should().Be("otp-user");
    }

    [Fact]
    public async Task GenerateVerificationArtifactsAsync_ShouldReplaceSignupState_WhenUsernameChanges()
    {
        var service = new TokenService(new InMemoryCacheService());
        var user = new User
        {
            Email = "signup@example.com",
            Username = "first-user",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var first = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);
        user.Username = "second-user";
        var second = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        second.LinkToken.Should().NotBe(first.LinkToken);
        var verified = await service.VerifyVerificationToken(
            second.LinkToken,
            VerificationPurpose.SignUp
        );
        verified.Username.Should().Be("second-user");
    }

    [Fact]
    public async Task VerifyVerificationToken_ShouldReturnUser_AndClearStoredState()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "verify@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);
        var verifiedUser = await service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.SignUp);

        verifiedUser.Email.Should().Be("verify@example.com");
        verifiedUser.Password.Should().Be("hashed-password");
        verifiedUser.Usertype.Should().Be("Organizer");
        (await service.VerificationTokenExist("verify@example.com", VerificationPurpose.SignUp)).Should().BeNull();
    }

    /// <summary>
    /// A reset whose new password is rejected must leave the link usable, or every typo in a new
    /// password would cost the user another round trip through their inbox.
    /// </summary>
    [Fact]
    public async Task VerifyVerificationToken_ShouldKeepTheToken_WhenBeforeConsumeThrows()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateVerificationArtifactsAsync(
            new User { Email = "reset@example.com", Password = "placeholder", Usertype = "placeholder" },
            VerificationPurpose.ResetPassword);

        var rejected = () => service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.ResetPassword,
            _ => throw new BadRequestException("rejected"));
        await rejected.Should().ThrowAsync<BadRequestException>();

        User? seen = null;
        var verified = await service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.ResetPassword,
            pending =>
            {
                seen = pending;
                return Task.CompletedTask;
            });

        seen!.Email.Should().Be("reset@example.com");
        verified.Email.Should().Be("reset@example.com");
        var replay = () => service.VerifyVerificationToken(artifacts.LinkToken, VerificationPurpose.ResetPassword);
        await replay.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task VerifyVerificationOtpAsync_ShouldKeepTheChallenge_WhenBeforeConsumeThrows()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateVerificationArtifactsAsync(
            new User { Email = "reset-otp@example.com", Password = "placeholder", Usertype = "placeholder" },
            VerificationPurpose.ResetPassword);

        var rejected = () => service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.ResetPassword,
            _ => throw new BadRequestException("rejected"));
        await rejected.Should().ThrowAsync<BadRequestException>();

        var verified = await service.VerifyVerificationOtpAsync(
            artifacts.OtpChallenge.Code,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.ResetPassword);

        verified.Email.Should().Be("reset-otp@example.com");
    }

    [Fact]
    public async Task VerifyVerificationOtpAsync_ShouldNotRunBeforeConsume_ForAWrongCode()
    {
        var service = new TokenService(new InMemoryCacheService());
        var artifacts = await service.GenerateVerificationArtifactsAsync(
            new User { Email = "wrong-code@example.com", Password = "placeholder", Usertype = "placeholder" },
            VerificationPurpose.ResetPassword);
        var wrongCode = artifacts.OtpChallenge.Code == "000000" ? "111111" : "000000";
        var ran = false;

        var act = () => service.VerifyVerificationOtpAsync(
            wrongCode,
            artifacts.OtpChallenge.Challenge,
            VerificationPurpose.ResetPassword,
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            });

        await act.Should().ThrowAsync<UnauthorizedException>();
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyVerificationToken_ShouldRejectPurposeMismatch()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var user = new User
        {
            Email = "verify@example.com",
            Password = "hashed-password",
            Usertype = "Organizer"
        };

        var artifacts = await service.GenerateVerificationArtifactsAsync(user, VerificationPurpose.SignUp);

        var act = () => service.VerifyVerificationToken(
            artifacts.LinkToken,
            VerificationPurpose.ResetPassword);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Verification token purpose mismatch.");
    }

    [Fact]
    public async Task RevokeAllRefreshSessionsAsync_ShouldInvalidateEverySessionForUser()
    {
        var cache = new InMemoryCacheService();
        var service = new TokenService(cache);
        var browserRequest = CreateRequestInfo();
        var apiRequest = new ClientRequestInfo
        {
            IpAddress = "10.0.0.9",
            ClientName = "ApiClient",
            DeviceType = "Server",
            IsBrowserClient = false
        };

        var browserIssue = await service.GenerateRefreshToken(44, 1, browserRequest, SessionTransport.BrowserCookie);
        var apiIssue = await service.GenerateRefreshToken(44, 1, apiRequest, SessionTransport.ApiToken);

        await service.RevokeAllRefreshSessionsAsync(44);

        var browserAct = () => service.ValidateRefreshToken(
            browserIssue.Value,
            browserIssue.SessionBindingToken,
            SessionTransport.BrowserCookie,
            browserRequest);
        var apiAct = () => service.ValidateRefreshToken(
            apiIssue.Value,
            apiIssue.SessionBindingToken,
            SessionTransport.ApiToken,
            apiRequest);

        await browserAct.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
        await apiAct.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid or expired refresh token.");
    }

    private static ClientRequestInfo CreateRequestInfo() => new()
    {
        IpAddress = "127.0.0.1",
        ClientName = "UnitTest",
        DeviceType = "Desktop",
        IsBrowserClient = true
    };

    private static string RefreshTokenKey(string refreshToken) =>
        $"refresh:v2:token:{CryptoHelper.HashToken(refreshToken)}";

    private static Task<RefreshTokenValidationResult> ValidateAsync(TokenService service, RefreshTokenIssue issue) =>
        service.ValidateRefreshToken(issue.Value, issue.SessionBindingToken, issue.Transport, CreateRequestInfo());

    /// <summary>Issues a token and rotates it once, as a refresh does.</summary>
    private static async Task<(RefreshTokenIssue First, RefreshTokenIssue Newest)> RotateAsync(TokenService service)
    {
        var first = await service.GenerateRefreshToken(8, 1, CreateRequestInfo(), SessionTransport.ApiToken);
        var validation = await ValidateAsync(service, first);
        var newest = await service.GenerateRefreshToken(
            8, 1, CreateRequestInfo(), SessionTransport.ApiToken, validation.SessionId);
        return (first, newest);
    }

    private const string LiveToken = "live-token";

    /// <summary>
    /// A cache mock holding <see cref="LiveToken"/> as a live stored (pre-derived) API-transport
    /// token and its session.
    /// </summary>
    private static Mock<backend.main.features.cache.ICacheService> CreateCacheWithLiveToken()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.GetValueAsync(RefreshTokenKey(LiveToken)))
            .ReturnsAsync("{\"SessionId\":\"s1\",\"UserId\":8}");
        cache.Setup(c => c.GetValueAsync("refresh:v2:session:s1"))
            .ReturnsAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                SessionId = "s1",
                UserId = 8,
                Transport = SessionTransport.ApiToken,
                CurrentRefreshTokenHash = CryptoHelper.HashToken(LiveToken),
                CurrentBindingTokenHash = CryptoHelper.HashToken("binding"),
            }));
        return cache;
    }

    /// <summary>
    /// Stores a session as it was before derived tokens: a random token under its hash, and a
    /// session with no token secret.
    /// </summary>
    private static async Task<RefreshTokenIssue> SeedLegacySessionAsync(InMemoryCacheService cache)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        const string token = "bGVnYWN5LXJlZnJlc2gtdG9rZW4tdmFsdWUtMDAwMDA=";
        const string binding = "legacy-binding";
        await cache.SetValueAsync(
            RefreshTokenKey(token),
            Newtonsoft.Json.JsonConvert.SerializeObject(new { SessionId = sessionId, UserId = 8, CreatedAt = DateTime.UtcNow }),
            TimeSpan.FromDays(1));
        await cache.SetValueAsync(
            $"refresh:v2:session:{sessionId}",
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                SessionId = sessionId,
                UserId = 8,
                AuthVersion = 1,
                Transport = SessionTransport.ApiToken,
                CurrentRefreshTokenHash = CryptoHelper.HashToken(token),
                CurrentBindingTokenHash = CryptoHelper.HashToken(binding),
            }),
            TimeSpan.FromDays(1));
        await cache.SetAddAsync("refresh:v2:user:8:sessions", sessionId);
        return new RefreshTokenIssue(token, binding, TimeSpan.FromDays(1), SessionTransport.ApiToken, sessionId);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

internal sealed class InMemoryCacheService : backend.main.features.cache.ICacheService
{
    private sealed class CacheEntry
    {
        public string? StringValue { get; set; }
        public Dictionary<string, string> HashValues { get; } = [];
        public HashSet<string> SetValues { get; } = [];
        public LinkedList<string> ListValues { get; } = [];
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = [];

    /// <summary>
    /// Runs once, just before the next script, to interleave a concurrent request with the
    /// read-then-swap a script completes.
    /// </summary>
    public Func<Task>? BeforeNextScript { get; set; }

    /// <summary>Makes scripts fail as they do when Redis is unreachable.</summary>
    public bool ScriptsUnavailable { get; set; }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _entries.Keys.Where(key => TryGetEntry(key, out _)).ToList();
            }
        }
    }

    public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.StringValue = value;
            entry.ExpiresAt = ResolveExpiry(expiry);
        }

        return Task.FromResult(true);
    }

    public Task<string?> GetValueAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) ? entry.StringValue : null);
        }
    }

    public Task<long> IncrementAsync(string key, long value = 1)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            var current = long.TryParse(entry.StringValue, out var parsed) ? parsed : 0L;
            current += value;
            entry.StringValue = current.ToString();
            return Task.FromResult(current);
        }
    }

    public Task<long> DecrementAsync(string key, long value = 1) => IncrementAsync(key, -value);

    public Task<bool> HashSetAsync(string key, string field, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.HashValues[field] = value;
            return Task.FromResult(true);
        }
    }

    public Task<string?> HashGetAsync(string key, string field)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || !entry.HashValues.TryGetValue(field, out var value))
                return Task.FromResult<string?>(null);

            return Task.FromResult<string?>(value);
        }
    }

    public Task<Dictionary<string, string>> HashGetAllAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry))
                return Task.FromResult(new Dictionary<string, string>());

            return Task.FromResult(entry.HashValues.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }

    public Task<bool> HashDeleteAsync(string key, string field)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) && entry.HashValues.Remove(field));
        }
    }

    public Task<bool> SetAddAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            return Task.FromResult(entry.SetValues.Add(value));
        }
    }

    public Task<bool> SetRemoveAsync(string key, string value)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry) && entry.SetValues.Remove(value));
        }
    }

    public Task<string[]> SetMembersAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out var entry)
                ? entry.SetValues.ToArray()
                : []);
        }
    }

    public Task<long> ListLeftPushAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.ListValues.AddFirst(value);
            return Task.FromResult((long)entry.ListValues.Count);
        }
    }

    public Task<long> ListRightPushAsync(string key, string value)
    {
        lock (_gate)
        {
            var entry = GetOrCreateEntry(key);
            entry.ListValues.AddLast(value);
            return Task.FromResult((long)entry.ListValues.Count);
        }
    }

    public Task<string?> ListLeftPopAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ListValues.First == null)
                return Task.FromResult<string?>(null);

            var value = entry.ListValues.First.Value;
            entry.ListValues.RemoveFirst();
            return Task.FromResult<string?>(value);
        }
    }

    public Task<string?> ListRightPopAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ListValues.Last == null)
                return Task.FromResult<string?>(null);

            var value = entry.ListValues.Last.Value;
            entry.ListValues.RemoveLast();
            return Task.FromResult<string?>(value);
        }
    }

    public Task<bool> DeleteKeyAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.Remove(key));
        }
    }

    public Task<bool> KeyExistsAsync(string key)
    {
        lock (_gate)
        {
            return Task.FromResult(TryGetEntry(key, out _));
        }
    }

    public Task<TimeSpan?> GetTTLAsync(string key)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.ExpiresAt is null)
                return Task.FromResult<TimeSpan?>(null);

            return Task.FromResult<TimeSpan?>(entry.ExpiresAt.Value - DateTimeOffset.UtcNow);
        }
    }

    public Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry))
                return Task.FromResult(false);

            entry.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);
            return Task.FromResult(true);
        }
    }

    public IEnumerable<string> ScanKeys(StackExchange.Redis.IServer server, string pattern)
    {
        lock (_gate)
        {
            return _entries.Keys.ToArray();
        }
    }

    public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        lock (_gate)
        {
            if (TryGetEntry(key, out _))
                return Task.FromResult(false);

            var entry = GetOrCreateEntry(key);
            entry.StringValue = value;
            entry.ExpiresAt = DateTimeOffset.UtcNow.Add(expiry);
            return Task.FromResult(true);
        }
    }

    public Task<bool> ReleaseLockAsync(string key, string value)
    {
        lock (_gate)
        {
            if (!TryGetEntry(key, out var entry) || entry.StringValue != value)
                return Task.FromResult(false);

            _entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    public StackExchange.Redis.IServer GetServer() => throw new NotSupportedException();

    public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            return Task.FromResult(keys.ToDictionary(key => key, key =>
            {
                return TryGetEntry(key, out var entry) ? entry.StringValue : null;
            }));
        }
    }

    public async Task<object> EvalAsync(string script, StackExchange.Redis.RedisKey[] keys, StackExchange.Redis.RedisValue[] values)
    {
        if (BeforeNextScript is { } concurrent)
        {
            BeforeNextScript = null;
            await concurrent();
        }

        if (ScriptsUnavailable)
            throw new StackExchange.Redis.RedisConnectionException(
                StackExchange.Redis.ConnectionFailureType.UnableToConnect, "Redis is unavailable.");

        return EvaluateScript(script, keys, values);
    }

    private object EvaluateScript(string script, StackExchange.Redis.RedisKey[] keys, StackExchange.Redis.RedisValue[] values)
    {
        lock (_gate)
        {
            if (keys.Length == 2 && script.Contains("redis.call('DEL'", StringComparison.Ordinal))
            {
                _entries.Remove(keys[0].ToString());
                _entries.Remove(keys[1].ToString());
                return 1L;
            }

            if (keys.Length == 2 && values.Length == 0 && script.Contains("or false", StringComparison.Ordinal))
            {
                // Atomic proof + generation read used by the step-up gate.
                var proofValue = TryGetEntry(keys[0].ToString(), out var proofEntry) ? proofEntry.StringValue : null;
                var generationValue = TryGetEntry(keys[1].ToString(), out var genEntry) ? genEntry.StringValue : null;
                return new object?[] { proofValue, generationValue };
            }

            if (keys.Length == 3 && script.Contains("redis.call('EXISTS'", StringComparison.Ordinal))
            {
                if (!TryGetEntry(keys[0].ToString(), out _))
                    return 0L;

                var currentGeneration = TryGetEntry(keys[2].ToString(), out var generationEntry)
                    && long.TryParse(generationEntry.StringValue, out var parsed)
                        ? parsed
                        : 0;
                if (currentGeneration != long.Parse(values[2].ToString()))
                    return 2L;

                var proof = GetOrCreateEntry(keys[1].ToString());
                proof.StringValue = values[0].ToString();
                proof.ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(long.Parse(values[1].ToString()));
                return 1L;
            }

            if (keys.Length == 1
                && values.Length == 3
                && script.Contains("redis.call('GET'", StringComparison.Ordinal)
                && script.Contains("redis.call('SET'", StringComparison.Ordinal))
            {
                // Compare-and-set used to retire a rotated refresh token.
                var key = keys[0].ToString();
                var current = TryGetEntry(key, out var existing) ? existing.StringValue : null;
                if (current != values[0].ToString())
                    return 0L;

                var entry = GetOrCreateEntry(key);
                entry.StringValue = values[1].ToString();
                entry.ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(long.Parse(values[2].ToString()));
                return 1L;
            }

            if (keys.Length == 1
                && values.Length == 2
                && script.Contains("redis.call('GET'", StringComparison.Ordinal)
                && script.Contains("redis.call('SET'", StringComparison.Ordinal))
            {
                var key = keys[0].ToString();
                var matchedWindow = long.Parse(values[0].ToString());
                var ttlMs = long.Parse(values[1].ToString());

                if (TryGetEntry(key, out var existing)
                    && long.TryParse(existing.StringValue, out var lastWindow)
                    && matchedWindow <= lastWindow)
                {
                    return 0L;
                }

                var entry = GetOrCreateEntry(key);
                entry.StringValue = matchedWindow.ToString();
                entry.ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(ttlMs);
                return 1L;
            }

            return 1L;
        }
    }

    // The token service does not use bitmaps; these exist so the fake still satisfies ICacheService.
    public Task<bool> SetBitsAsync(string key, IReadOnlyCollection<long> bitPositions) =>
        Task.FromResult(false);

    public Task<byte[]?> GetBitmapAsync(string key) => Task.FromResult<byte[]?>(null);

    public Task<bool> SetBitmapAsync(string key, byte[] bitmap, TimeSpan? expiry = null) =>
        Task.FromResult(false);

    private CacheEntry GetOrCreateEntry(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new CacheEntry();
            _entries[key] = entry;
        }

        return entry;
    }

    private bool TryGetEntry(string key, out CacheEntry entry)
    {
        if (_entries.TryGetValue(key, out entry!))
        {
            if (entry.ExpiresAt is not null && entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key);
                entry = null!;
                return false;
            }

            return true;
        }

        entry = null!;
        return false;
    }

    private static DateTimeOffset? ResolveExpiry(TimeSpan? expiry) =>
        expiry is null ? null : DateTimeOffset.UtcNow.Add(expiry.Value);
}


