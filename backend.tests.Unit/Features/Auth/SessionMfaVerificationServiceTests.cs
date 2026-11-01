using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

using Newtonsoft.Json;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Auth;

public class SessionMfaVerificationServiceTests
{
    private const int UserId = 77;
    private const int AuthVersion = 3;
    private const string Email = "member@example.com";
    private const string SessionId = "session-xyz";
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task VerifyAsync_WithEmailCode_ShouldRecordBoundProof()
    {
        var notifications = new Mock<IAuthNotificationService>();
        string? sentCode = null;
        notifications
            .Setup(n => n.SendEmailMfaCodeAsync(Email, It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string, string?>((_, code, _) => sentCode = code)
            .Returns(Task.CompletedTask);

        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, cache, _) = CreateService(notifications: notifications, securityEvents: securityEvents);

        await service.StartAsync(UserId, Email, "email");
        sentCode.Should().NotBeNullOrWhiteSpace();

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "email", sentCode!);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();
        var proof = await ReadProofAsync(cache);
        proof.UserId.Should().Be(UserId);
        proof.SessionId.Should().Be(SessionId);
        proof.AuthVersion.Should().Be(AuthVersion);
        proof.Method.Should().Be("email");
        proof.VerifiedAtUtc.Should().Be(Start.UtcDateTime);
        var verified = securityEvents.Of(AuthSecurityEvents.StepUpSucceeded).Should().ContainSingle().Subject;
        verified.Method.Should().Be(AuthSecurityMethod.Email);
        verified.Flow.Should().Be(AuthSecurityFlow.SessionStepUp);
        verified.UserId.Should().Be(UserId);
        verified.SessionId.Should().Be(SessionId);
    }

    [Fact]
    public async Task VerifyAsync_WithSmsCode_ShouldRecordSmsProof()
    {
        var notifications = new Mock<IAuthNotificationService>();
        string? sentCode = null;
        notifications
            .Setup(n => n.SendSmsMfaAsync(
                "+15555550123",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>()))
            .Callback<string, string, string, DateTime, string>((_, code, _, _, _) => sentCode = code)
            .Returns(Task.CompletedTask);
        var smsRepository = new Mock<IMfaEnrollmentRepository>();
        smsRepository.Setup(r => r.GetByUserIdAsync(UserId)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = UserId,
            PhoneNumber = "+15555550123",
            IsSmsMfaEnabled = true,
        });

        var (service, cache, _) = CreateService(notifications: notifications, smsRepository: smsRepository);

        await service.StartAsync(UserId, Email, "sms");
        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "sms", sentCode!);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();
        (await ReadProofAsync(cache)).Method.Should().Be("sms");
    }

    [Fact]
    public async Task VerifyAsync_WithWrongEmailCode_ShouldThrow_AndNotRecordProof()
    {
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendEmailMfaCodeAsync(Email, It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, _, _) = CreateService(notifications: notifications, securityEvents: securityEvents);

        await service.StartAsync(UserId, Email, "email");

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "email", "000000");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
        securityEvents.Of(AuthSecurityEvents.StepUpFailed).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.InvalidCode);
        securityEvents.Of(AuthSecurityEvents.StepUpSucceeded).Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_WithValidTotp_ShouldRecordTotpProof()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);

        var (service, cache, _) = CreateService(totp: totp);

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();
        (await ReadProofAsync(cache)).Method.Should().Be("totp");
        totp.Verify(t => t.VerifyPersistedCodeAsync(UserId, "123456"), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WithInvalidTotp_ShouldPropagate_AndNotRecordProof()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedException("Invalid or expired TOTP code."));

        var (service, _, _) = CreateService(totp: totp);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "999999");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithMalformedTotp_ShouldRecordAFailedStepUp()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "abc"))
            .ThrowsAsync(new BadRequestException("TOTP code must be 6 digits."));
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, _, _) = CreateService(totp: totp, securityEvents: securityEvents);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "abc");

        await act.Should().ThrowAsync<BadRequestException>();
        securityEvents.Of(AuthSecurityEvents.StepUpFailed).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.InvalidRequest);
    }

    [Fact]
    public async Task VerifyAsync_WithRepeatedInvalidTotp_ShouldThrottleAfterMaxAttempts()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedException("Invalid or expired TOTP code."));

        var (service, _, _) = CreateService(totp: totp);

        // The first four failures surface as Unauthorized; the fifth trips the throttle.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var invalid = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "000000");
            await invalid.Should().ThrowAsync<UnauthorizedException>();
        }

        var throttled = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "000000");
        await throttled.Should().ThrowAsync<TooManyRequestException>();

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithoutSessionId_ShouldThrow()
    {
        var (service, _, _) = CreateService();

        var act = () => service.VerifyAsync(UserId, Email, string.Empty, AuthVersion, "totp", "123456");

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task VerifyAsync_ShouldStoreProofAgainstTheSession_WithAbsoluteLifetime()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var tokenService = new Mock<ITokenService>();

        var (service, _, _) = CreateService(totp: totp, tokenService: tokenService, lifetimeMinutes: 7);

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        tokenService.Verify(
            t => t.StoreStepUpProofAsync(SessionId, UserId, 0L, It.IsAny<string>(), TimeSpan.FromMinutes(7)),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_ShouldFail_WhenTheSessionEndedBeforeTheProofWasStored()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, _, _) = CreateService(
            totp: totp,
            storeResult: StepUpProofWriteResult.SessionEnded,
            securityEvents: securityEvents);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
        securityEvents.Of(AuthSecurityEvents.StepUpFailed).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.SessionEnded);
        securityEvents.Of(AuthSecurityEvents.StepUpSucceeded).Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_ShouldReportUnavailable_WhenTheProofCannotBeStored()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var (service, _, _) = CreateService(totp: totp, storeResult: StepUpProofWriteResult.Unavailable);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldNotExtendTheWindow()
    {
        var cache = ProofCache(SerializeProof(verifiedAt: Start.UtcDateTime));

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();

        cache.Verify(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        cache.Verify(
            c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldExpireTenMinutesAfterVerification_EvenWhenUsed()
    {
        var (service, _, time) = await CreateVerifiedServiceAsync();

        time.Now = Start.AddMinutes(9).AddSeconds(59);
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();

        time.Now = Start.AddMinutes(10);
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldHonourConfiguredShorterLifetime()
    {
        var (service, _, time) = await CreateVerifiedServiceAsync(lifetimeMinutes: 2);

        time.Now = Start.AddMinutes(2);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_ProofDatedInTheFuture()
    {
        var (service, _, time) = await CreateVerifiedServiceAsync();

        time.Now = Start.AddMinutes(-1);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_DifferentUser()
    {
        var (service, _, _) = await CreateVerifiedServiceAsync();

        (await service.HasFreshProofAsync(UserId + 1, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_DifferentSession()
    {
        var (service, _, _) = await CreateVerifiedServiceAsync();

        (await service.HasFreshProofAsync(UserId, "another-session", AuthVersion)).Should().BeFalse();
        (await service.HasFreshProofAsync(UserId, null, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_ProofWhoseRecordedSessionDiffers()
    {
        var cache = ProofCache(SerializeProof(verifiedAt: Start.UtcDateTime, sessionId: "copied-from-elsewhere"));

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldRejectAndDelete_WhenAuthVersionChanged()
    {
        var (service, cache, _) = await CreateVerifiedServiceAsync();

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion + 1)).Should().BeFalse();

        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(SessionId))).Should().BeFalse();
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_UnreadableProof()
    {
        var cache = ProofCache("1");

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task ClearSessionProofAsync_ShouldRemoveTheProof()
    {
        var (service, _, _) = await CreateVerifiedServiceAsync();

        await service.ClearSessionProofAsync(SessionId);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task ClearUserProofsAsync_ShouldRetireProofsOnEverySession()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);

        var (service, cache, _) = CreateService(totp: totp);
        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");
        await service.VerifyAsync(UserId, Email, "session-two", AuthVersion, "totp", "123456");

        await service.ClearUserProofsAsync(UserId);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
        (await service.HasFreshProofAsync(UserId, "session-two", AuthVersion)).Should().BeFalse();
        (await cache.GetValueAsync(StepUpProofKeys.GenerationForUser(UserId))).Should().Be("1");
    }

    [Fact]
    public async Task ClearUserProofsAsync_ShouldAllowANewProof_AfterTheFactorChange()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var (service, _, _) = CreateService(totp: totp);
        await service.ClearUserProofsAsync(UserId);

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ShouldBeSuperseded_WhenAFactorChangeCompletesMidVerification()
    {
        // The code checks out, but a factor change finishes before the proof is written.
        InMemoryCacheService? cacheRef = null;
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456"))
            .Returns(async () => await cacheRef!.IncrementAsync(StepUpProofKeys.GenerationForUser(UserId)));
        var (service, cache, _) = CreateService(totp: totp);
        cacheRef = cache;

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        await act.Should().ThrowAsync<ConflictException>();
        (await cache.KeyExistsAsync(StepUpProofKeys.ForSession(SessionId))).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_ProofFromAnEarlierGeneration()
    {
        var cache = ProofCache(SerializeProof(verifiedAt: Start.UtcDateTime), generation: "3");

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldFailClosed_WhenTheCacheCannotAnswer()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.TryEvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync(backend.main.features.cache.CacheScriptResult.Unavailable);

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_WhenTheGenerationIsMissingButTheProofIsLater()
    {
        // A proof minted at generation 2 must not pass if the generation now reads as absent.
        var cache = ProofCache(SerializeProof(verifiedAt: Start.UtcDateTime, generation: 2), generation: null);

        var service = CreateService(cache.Object).service;

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldReject_AfterACommittedFactorChange_EvenWithoutAGenerationBump()
    {
        // The factor change commits to the database, but the cache-side generation bump never
        // happened (e.g. Redis was down). The durable factor state still retires the proof.
        var smsRepository = new Mock<IMfaEnrollmentRepository>();
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var (service, _, _) = CreateService(totp: totp, smsRepository: smsRepository);
        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();

        smsRepository.Setup(r => r.GetByUserIdAsync(UserId)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = UserId,
            PhoneNumber = "+15555550123",
            IsSmsMfaEnabled = true,
            UpdatedAt = Start.UtcDateTime.AddMinutes(1),
        });

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
    }

    [Fact]
    public async Task ClearUserProofsAsync_ShouldFail_WhenTheGenerationCannotBeAdvanced()
    {
        // CacheService reports an unreachable Redis as 0; a real INCR never returns 0.
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.IncrementAsync(StepUpProofKeys.GenerationForUser(UserId), 1)).ReturnsAsync(0);

        var service = CreateService(cache.Object).service;

        var act = () => service.ClearUserProofsAsync(UserId);

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task GetOptionsAsync_ShouldOfferRecoveryCodes_OnlyWhileSomeRemain_AlongsideEmail()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        var (service, _, _) = CreateService(recoveryCodes: recoveryCodes);

        (await service.GetOptionsAsync(UserId, Email)).AvailableMethods.Should().Equal("email");

        recoveryCodes.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(true);
        (await service.GetOptionsAsync(UserId, Email)).AvailableMethods.Should().Equal("recovery_code", "email");
    }

    [Fact]
    public async Task StartAsync_WithRecoveryCode_ShouldNeitherDeliverNorStoreAnything()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        recoveryCodes.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(true);
        var notifications = new Mock<IAuthNotificationService>();
        var (service, _, _) = CreateService(notifications: notifications, recoveryCodes: recoveryCodes);

        var started = await service.StartAsync(UserId, Email, "RECOVERY_CODE");

        started.SelectedMethod.Should().Be("recovery_code");
        started.MaskedDestination.Should().Be("recovery code");
        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartAsync_WithRecoveryCode_ShouldBeRefused_WhenNoneRemain()
    {
        var (service, _, _) = CreateService();

        var act = () => service.StartAsync(UserId, Email, "recovery_code");

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task VerifyAsync_WithRecoveryCode_ShouldRecordTheProof_AndTheCodesUse()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        recoveryCodes.Setup(r => r.TryConsumeAsync(UserId, "good-code")).ReturnsAsync(true);
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, cache, _) = CreateService(securityEvents: securityEvents, recoveryCodes: recoveryCodes);

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "recovery_code", "good-code");

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeTrue();
        (await ReadProofAsync(cache)).Method.Should().Be("recovery_code");
        var used = securityEvents.Of(AuthSecurityEvents.RecoveryCodeUsed).Should().ContainSingle().Subject;
        used.UserId.Should().Be(UserId);
        used.SessionId.Should().Be(SessionId);
        used.Flow.Should().Be(AuthSecurityFlow.SessionStepUp);
        used.Method.Should().Be(AuthSecurityMethod.RecoveryCode);
        securityEvents.Of(AuthSecurityEvents.StepUpSucceeded).Should().ContainSingle()
            .Which.Method.Should().Be(AuthSecurityMethod.RecoveryCode);
    }

    [Fact]
    public async Task VerifyAsync_WithAWrongOrSpentRecoveryCode_ShouldThrow_AndThrottleAfterMaxAttempts()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, _, _) = CreateService(securityEvents: securityEvents, recoveryCodes: recoveryCodes);

        for (var attempt = 1; attempt < 5; attempt++)
        {
            var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "recovery_code", "spent-code");
            await act.Should().ThrowAsync<UnauthorizedException>();
        }

        var throttled = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "recovery_code", "spent-code");
        await throttled.Should().ThrowAsync<TooManyRequestException>();

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
        securityEvents.Of(AuthSecurityEvents.StepUpFailed).Should().HaveCount(4)
            .And.OnlyContain(e => e.Reason == AuthSecurityReason.InvalidCode && e.Method == AuthSecurityMethod.RecoveryCode);
        securityEvents.Of(AuthSecurityEvents.Throttled).Should().ContainSingle();
        securityEvents.Of(AuthSecurityEvents.RecoveryCodeUsed).Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_WithMalformedRecoveryCode_ShouldNotCountAnAttempt()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        recoveryCodes.Setup(r => r.TryConsumeAsync(UserId, "nope")).ThrowsAsync(new BadRequestException("malformed"));
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, cache, _) = CreateService(securityEvents: securityEvents, recoveryCodes: recoveryCodes);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "recovery_code", "nope");

        await act.Should().ThrowAsync<BadRequestException>();
        (await cache.GetValueAsync($"mfa:stepup:recovery-attempts:user:{UserId}")).Should().BeNull();
        securityEvents.Of(AuthSecurityEvents.StepUpFailed).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.InvalidRequest);
    }

    [Fact]
    public async Task VerifyAsync_WithRecoveryCode_ShouldStillRecordTheUse_WhenTheSessionEndedMeanwhile()
    {
        var recoveryCodes = new Mock<IRecoveryCodeService>();
        recoveryCodes.Setup(r => r.TryConsumeAsync(UserId, "good-code")).ReturnsAsync(true);
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var (service, _, _) = CreateService(
            storeResult: StepUpProofWriteResult.SessionEnded,
            securityEvents: securityEvents,
            recoveryCodes: recoveryCodes);

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "recovery_code", "good-code");

        await act.Should().ThrowAsync<UnauthorizedException>();
        securityEvents.Of(AuthSecurityEvents.RecoveryCodeUsed).Should().ContainSingle();
    }

    [Fact]
    public async Task GetOptionsAsync_ShouldAlwaysOfferEmail()
    {
        var (service, _, _) = CreateService();

        var options = await service.GetOptionsAsync(UserId, Email);

        options.AvailableMethods.Should().Contain("email");
        options.MaskedEmail.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<(SessionMfaVerificationService service, InMemoryCacheService cache, MutableTimeProvider time)>
        CreateVerifiedServiceAsync(int lifetimeMinutes = 10)
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);

        var created = CreateService(totp: totp, lifetimeMinutes: lifetimeMinutes);
        await created.service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");
        return created;
    }

    private static (SessionMfaVerificationService service, InMemoryCacheService cache, MutableTimeProvider time) CreateService(
        Mock<IAuthNotificationService>? notifications = null,
        Mock<ITotpMfaEnrollmentService>? totp = null,
        Mock<IMfaEnrollmentRepository>? smsRepository = null,
        Mock<ITokenService>? tokenService = null,
        int lifetimeMinutes = 10,
        StepUpProofWriteResult? storeResult = null,
        IAuthSecurityEventEmitter? securityEvents = null,
        Mock<IRecoveryCodeService>? recoveryCodes = null)
    {
        var cache = new InMemoryCacheService();
        var (service, time) = CreateService(
            cache, notifications, totp, smsRepository, tokenService, lifetimeMinutes, storeResult, securityEvents, recoveryCodes);
        return (service, cache, time);
    }

    private static (SessionMfaVerificationService service, MutableTimeProvider time) CreateService(
        backend.main.features.cache.ICacheService cache,
        Mock<IAuthNotificationService>? notifications = null,
        Mock<ITotpMfaEnrollmentService>? totp = null,
        Mock<IMfaEnrollmentRepository>? smsRepository = null,
        Mock<ITokenService>? tokenService = null,
        int lifetimeMinutes = 10,
        StepUpProofWriteResult? storeResult = null,
        IAuthSecurityEventEmitter? securityEvents = null,
        Mock<IRecoveryCodeService>? recoveryCodes = null)
    {
        notifications ??= new Mock<IAuthNotificationService>();
        totp ??= new Mock<ITotpMfaEnrollmentService>();
        smsRepository ??= new Mock<IMfaEnrollmentRepository>();
        tokenService ??= new Mock<ITokenService>();
        // Stand in for a live refresh session by storing the proof in the shared test cache,
        // unless the test asks for a specific store outcome.
        tokenService
            .Setup(t => t.StoreStepUpProofAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns<string, int, long, string, TimeSpan>(async (sessionId, userId, expectedGeneration, json, ttl) =>
            {
                if (storeResult is { } forced)
                    return forced;

                // Mirror the Redis script: refuse the write if the generation moved meanwhile.
                var current = await cache.GetValueAsync(StepUpProofKeys.GenerationForUser(userId));
                if ((long.TryParse(current, out var generation) ? generation : 0) != expectedGeneration)
                    return StepUpProofWriteResult.Superseded;

                await cache.SetValueAsync(StepUpProofKeys.ForSession(sessionId), json, ttl);
                return StepUpProofWriteResult.Stored;
            });
        var time = new MutableTimeProvider(Start);

        var service = new SessionMfaVerificationService(
            cache,
            notifications.Object,
            smsRepository.Object,
            totp.Object,
            tokenService.Object,
            time,
            Options.Create(new StepUpOptions { ProofLifetimeMinutes = lifetimeMinutes }),
            securityEvents ?? NullAuthSecurityEventEmitter.Instance,
            (recoveryCodes ?? new Mock<IRecoveryCodeService>()).Object);

        return (service, time);
    }

    private static async Task<StepUpProof> ReadProofAsync(InMemoryCacheService cache)
    {
        var json = await cache.GetValueAsync(StepUpProofKeys.ForSession(SessionId));
        return JsonConvert.DeserializeObject<StepUpProof>(json!)!;
    }

    private static string SerializeProof(DateTime verifiedAt, string sessionId = SessionId, long generation = 0) =>
        JsonConvert.SerializeObject(new StepUpProof
        {
            UserId = UserId,
            SessionId = sessionId,
            AuthVersion = AuthVersion,
            Method = "email",
            VerifiedAtUtc = verifiedAt,
            Generation = generation,
            // Matches an account with no committed SMS or TOTP enrollment (the default mocks).
            FactorState = StepUpFactorState.From(null, null),
        });

    /// <summary>A cache whose atomic proof read returns the given proof and generation.</summary>
    private static Mock<backend.main.features.cache.ICacheService> ProofCache(string? proofJson, string? generation = null)
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.TryEvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync(new backend.main.features.cache.CacheScriptResult(true, new object?[] { proofJson, generation }));
        return cache;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
