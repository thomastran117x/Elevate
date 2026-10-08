using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

using Newtonsoft.Json;

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

        var (service, cache, _) = CreateService(notifications: notifications);

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

        var (service, _, _) = CreateService(notifications: notifications);

        await service.StartAsync(UserId, Email, "email");

        var act = () => service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "email", "000000");

        await act.Should().ThrowAsync<UnauthorizedException>();
        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
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
    public async Task VerifyAsync_ShouldStoreProofWithAbsoluteLifetimeTtl()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);

        var service = CreateService(cache.Object, totp: totp, lifetimeMinutes: 7).service;

        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");

        cache.Verify(
            c => c.SetValueAsync(StepUpProofKeys.ForSession(SessionId), It.IsAny<string>(), TimeSpan.FromMinutes(7)),
            Times.Once);
    }

    [Fact]
    public async Task HasFreshProofAsync_ShouldNotExtendTheWindow()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.GetValueAsync(StepUpProofKeys.ForSession(SessionId)))
            .ReturnsAsync(SerializeProof(verifiedAt: Start.UtcDateTime));

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
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.GetValueAsync(StepUpProofKeys.ForSession(SessionId)))
            .ReturnsAsync(SerializeProof(verifiedAt: Start.UtcDateTime, sessionId: "copied-from-elsewhere"));

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
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(c => c.GetValueAsync(StepUpProofKeys.ForSession(SessionId))).ReturnsAsync("1");

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
    public async Task ClearUserProofsAsync_ShouldRemoveProofsOnEverySession()
    {
        var totp = new Mock<ITotpMfaEnrollmentService>();
        totp.Setup(t => t.VerifyPersistedCodeAsync(UserId, "123456")).Returns(Task.CompletedTask);
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(t => t.GetSessionIdsAsync(UserId)).ReturnsAsync([SessionId, "session-two"]);

        var (service, _, _) = CreateService(totp: totp, tokenService: tokenService);
        await service.VerifyAsync(UserId, Email, SessionId, AuthVersion, "totp", "123456");
        await service.VerifyAsync(UserId, Email, "session-two", AuthVersion, "totp", "123456");

        await service.ClearUserProofsAsync(UserId);

        (await service.HasFreshProofAsync(UserId, SessionId, AuthVersion)).Should().BeFalse();
        (await service.HasFreshProofAsync(UserId, "session-two", AuthVersion)).Should().BeFalse();
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
        int lifetimeMinutes = 10)
    {
        var cache = new InMemoryCacheService();
        var (service, time) = CreateService(cache, notifications, totp, smsRepository, tokenService, lifetimeMinutes);
        return (service, cache, time);
    }

    private static (SessionMfaVerificationService service, MutableTimeProvider time) CreateService(
        backend.main.features.cache.ICacheService cache,
        Mock<IAuthNotificationService>? notifications = null,
        Mock<ITotpMfaEnrollmentService>? totp = null,
        Mock<IMfaEnrollmentRepository>? smsRepository = null,
        Mock<ITokenService>? tokenService = null,
        int lifetimeMinutes = 10)
    {
        notifications ??= new Mock<IAuthNotificationService>();
        totp ??= new Mock<ITotpMfaEnrollmentService>();
        smsRepository ??= new Mock<IMfaEnrollmentRepository>();
        tokenService ??= new Mock<ITokenService>();
        var time = new MutableTimeProvider(Start);

        var service = new SessionMfaVerificationService(
            cache,
            notifications.Object,
            smsRepository.Object,
            totp.Object,
            tokenService.Object,
            time,
            Options.Create(new StepUpOptions { ProofLifetimeMinutes = lifetimeMinutes }));

        return (service, time);
    }

    private static async Task<StepUpProof> ReadProofAsync(InMemoryCacheService cache)
    {
        var json = await cache.GetValueAsync(StepUpProofKeys.ForSession(SessionId));
        return JsonConvert.DeserializeObject<StepUpProof>(json!)!;
    }

    private static string SerializeProof(DateTime verifiedAt, string sessionId = SessionId) =>
        JsonConvert.SerializeObject(new StepUpProof
        {
            UserId = UserId,
            SessionId = sessionId,
            AuthVersion = AuthVersion,
            Method = "email",
            VerifiedAtUtc = verifiedAt,
        });

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
