using System.Net;
using System.Security.Claims;

using backend.main.features.auth.events;
using backend.main.features.auth.token;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace backend.tests.Unit.Features.Auth.Events;

public sealed class AuthSecurityEventEmitterTests
{
    private const string SessionId = "0f8fad5bd9cb469fa16570867728950e";

    [Fact]
    public void Emit_ShouldEnrichFromTheRequest()
    {
        var context = CreateContext(userId: 42, sessionId: SessionId);
        context.Request.Headers.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Mobile/15E148 Safari/604.1";
        context.TraceIdentifier = "req-123";
        var sink = new RecordingAuthSecurityEventSink();
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(
            context,
            [sink],
            timeProvider: new FixedTimeProvider(now));

        emitter.Emit(
            new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.StepUpSucceeded,
                Outcome = AuthSecurityOutcome.Succeeded,
                Method = AuthSecurityMethod.Totp,
            });

        var recorded = sink.Events.Should().ContainSingle().Subject;
        recorded.UserId.Should().Be(42);
        recorded.SessionId.Should().Be(SessionId);
        recorded.CorrelationId.Should().Be("req-123");
        recorded.ClientClass.Should().Be(AuthClientClass.Browser);
        recorded.DeviceClass.Should().Be(AuthDeviceClass.Mobile);
        recorded.SourceIpHash.Should().MatchRegex("^[0-9a-f]{32}$");
        recorded.OccurredAt.Should().Be(now);
    }

    [Fact]
    public void Emit_ShouldKeepTheCallersSessionOutOfEventsAboutAnotherAccount()
    {
        var context = CreateContext(userId: 1, sessionId: SessionId);
        var sink = new RecordingAuthSecurityEventSink();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(context, [sink]);

        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SessionRevoked,
            Outcome = AuthSecurityOutcome.Succeeded,
            Reason = AuthSecurityReason.AccountDisabled,
            UserId = 7,
            ActorUserId = 1,
        });

        var recorded = sink.Events.Should().ContainSingle().Subject;
        recorded.UserId.Should().Be(7);
        recorded.ActorUserId.Should().Be(1);
        recorded.SessionId.Should().BeNull();
    }

    [Fact]
    public void Emit_ShouldOverwriteEnrichmentAndDropMalformedValues()
    {
        var context = CreateContext();
        context.TraceIdentifier = "line\r\nbreak";
        var sink = new RecordingAuthSecurityEventSink();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(context, [sink]);

        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInFailed,
            Outcome = AuthSecurityOutcome.Denied,
            SessionId = "not-a-session",
            TargetHash = "caller supplied",
            CorrelationId = "caller supplied",
        });

        var recorded = sink.Events.Should().ContainSingle().Subject;
        recorded.SessionId.Should().BeNull();
        recorded.TargetHash.Should().BeNull();
        recorded.CorrelationId.Should().BeNull();
        recorded.ClientClass.Should().Be(AuthClientClass.Unknown);
    }

    [Fact]
    public void Emit_ShouldHashTheTargetAndHonorTheIncludeSwitches()
    {
        var sink = new RecordingAuthSecurityEventSink();
        var options = AuthSecurityEventTestSupport.Options();
        options.IncludeSourceIpHash = false;
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(CreateContext(), [sink], options: options);

        emitter.Emit(SignInFailed(), AuthSecurityTarget.Username("Ada"));

        var recorded = sink.Events.Should().ContainSingle().Subject;
        recorded.TargetHash.Should()
            .Be(AuthSecurityEventTestSupport.Hasher().HashTarget(AuthSecurityTarget.Username("ada")));
        recorded.SourceIpHash.Should().BeNull();
    }

    [Fact]
    public void Emit_ShouldWorkWithoutARequest()
    {
        var sink = new RecordingAuthSecurityEventSink();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(null, [sink]);

        emitter.Emit(SignInFailed());

        var recorded = sink.Events.Should().ContainSingle().Subject;
        recorded.CorrelationId.Should().BeNull();
        recorded.SourceIpHash.Should().BeNull();
    }

    [Fact]
    public void Emit_ShouldIsolateAFailingSink()
    {
        var after = new RecordingAuthSecurityEventSink();
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var metrics = new AuthMetricsRecorder(meterFactory);
        var logger = new ListLogger<AuthSecurityEventEmitter>();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(
            CreateContext(),
            [new ThrowingAuthSecurityEventSink(), after],
            new AuthSecurityMetrics(meterFactory),
            logger: logger);

        var emit = () => emitter.Emit(SignInFailed());

        emit.Should().NotThrow();
        after.Events.Should().ContainSingle();
        metrics.Measurements.Should().ContainSingle(measurement =>
            measurement.Instrument == "auth.security_events.sink_failures"
            && (string?)measurement.Tags["sink"] == nameof(ThrowingAuthSecurityEventSink));
        var warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().NotContain("secret-in-message");
    }

    [Fact]
    public void LoggingSink_ShouldWriteTheCatalogEventIdAndLevel()
    {
        var logger = new ListLogger<LoggingAuthSecurityEventSink>();
        var sink = new LoggingAuthSecurityEventSink(logger, AuthSecurityEventTestSupport.Monitor());

        sink.Write(SignInFailed() with { Reason = AuthSecurityReason.InvalidCredentials, UserId = 9 });

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(33002);
        entry.EventId.Name.Should().Be("auth.sign_in.failed");
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("auth.sign_in.failed").And.Contain("reason=invalid_credentials").And.Contain("user=9");
        entry.State.Should().Contain(new KeyValuePair<string, object?>("Reason", "invalid_credentials"));
    }

    [Fact]
    public void Sinks_ShouldHonorTheirEnableSwitches()
    {
        var options = AuthSecurityEventTestSupport.Options();
        options.LogSinkEnabled = false;
        options.MetricsSinkEnabled = false;
        var logger = new ListLogger<LoggingAuthSecurityEventSink>();
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var metrics = new AuthMetricsRecorder(meterFactory);

        new LoggingAuthSecurityEventSink(logger, AuthSecurityEventTestSupport.Monitor(options)).Write(SignInFailed());
        new MetricsAuthSecurityEventSink(new AuthSecurityMetrics(meterFactory), AuthSecurityEventTestSupport.Monitor(options))
            .Write(SignInFailed());

        logger.Entries.Should().BeEmpty();
        metrics.Measurements.Should().BeEmpty();
    }

    [Fact]
    public void Pipeline_ShouldNeverCaptureProhibitedValues()
    {
        // Canary values standing in for everything the issue prohibits. They reach the emitter the
        // way they could in production: as the request's own headers, address, and identifiers.
        const string password = "Canary-Password-Value-91";
        const string otp = "493817";
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiI0MiJ9.canary-signature";
        const string refreshToken = "v1.0f8fad5bd9cb469fa16570867728950e.3.canaryMac";
        const string bindingToken = "canary-binding-token-value";
        const string deviceToken = "canary-device-token-value";
        const string captchaToken = "canary-captcha-token-value";
        const string email = "canary.person@example.com";
        const string phone = "+15555550123";
        const string ip = "203.0.113.77";
        string[] prohibited = [password, otp, jwt, refreshToken, bindingToken, deviceToken, captchaToken, email, phone, ip, "canary.person"];

        var context = CreateContext(userId: 42, sessionId: SessionId, ip: ip);
        context.Request.Headers.Authorization = $"Bearer {jwt}";
        context.Request.Headers.Cookie = $"refreshToken={refreshToken}; sessionBinding={bindingToken}; trustedDevice={deviceToken}";
        context.Request.Headers["X-Captcha-Token"] = captchaToken;
        context.Request.Headers.UserAgent = $"curl/8.0 {email} {phone} {otp}";
        context.TraceIdentifier = "req-1";

        var logger = new ListLogger<LoggingAuthSecurityEventSink>();
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var metrics = new AuthMetricsRecorder(meterFactory);
        var authMetrics = new AuthSecurityMetrics(meterFactory);
        var recording = new RecordingAuthSecurityEventSink();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(
            context,
            [
                new LoggingAuthSecurityEventSink(logger, AuthSecurityEventTestSupport.Monitor()),
                new MetricsAuthSecurityEventSink(authMetrics, AuthSecurityEventTestSupport.Monitor()),
                recording,
            ],
            authMetrics);

        foreach (var definition in AuthSecurityEvents.All)
        {
            emitter.Emit(
                new AuthSecurityEvent
                {
                    Definition = definition,
                    Outcome = AuthSecurityOutcome.Denied,
                    Reason = AuthSecurityReason.InvalidCredentials,
                    Method = AuthSecurityMethod.Password,
                    Flow = AuthSecurityFlow.Login,
                },
                AuthSecurityTarget.Email(email));
        }

        var captured = string.Join(
            "\n",
            logger.Entries.Select(entry => entry.Message)
                .Concat(logger.Entries.SelectMany(entry => entry.State.Select(pair => $"{pair.Key}={pair.Value}")))
                .Concat(recording.Events.Select(securityEvent => securityEvent.ToString()))
                .Concat(metrics.Measurements.SelectMany(measurement =>
                    measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"))));

        logger.Entries.Should().HaveCount(AuthSecurityEvents.All.Count);
        foreach (var value in prohibited)
            captured.Should().NotContainEquivalentOf(value);
    }

    [Fact]
    public void Metrics_ShouldOnlyUseBoundedTags()
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var metrics = new AuthSecurityMetrics(meterFactory);
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(
            CreateContext(userId: 42, sessionId: SessionId),
            [new MetricsAuthSecurityEventSink(metrics, AuthSecurityEventTestSupport.Monitor())],
            metrics);

        foreach (var definition in AuthSecurityEvents.All)
        {
            foreach (var outcome in Enum.GetValues<AuthSecurityOutcome>())
            {
                emitter.Emit(
                    new AuthSecurityEvent
                    {
                        Definition = definition,
                        Outcome = outcome,
                        Reason = AuthSecurityReason.TokenReuse,
                        Method = AuthSecurityMethod.RefreshToken,
                        Flow = AuthSecurityFlow.Refresh,
                        Dimension = AuthThrottleDimension.Account,
                        Factor = AuthSecurityFactor.Totp,
                        UserId = 42,
                        ActorUserId = 7,
                        SessionId = SessionId,
                        Route = "GET api/clubs/{id}",
                    },
                    AuthSecurityTarget.Email("someone@example.com"));
            }
        }

        metrics.RecordOperation(AuthOperation.SignIn, AuthSecurityOutcome.Faulted, TimeSpan.FromMilliseconds(12));

        string[] allowedKeys = ["event", "outcome", "reason", "method", "flow", "dimension", "factor", "operation", "sink"];
        recorder.Measurements.Should().NotBeEmpty();
        foreach (var (_, _, tags) in recorder.Measurements)
        {
            tags.Keys.Should().BeSubsetOf(allowedKeys);
            foreach (var value in tags.Values.Select(tagValue => tagValue?.ToString() ?? string.Empty))
            {
                value.Should().MatchRegex("^[a-z_.]+$", "tag values must come from closed sets");
                int.TryParse(value, out _).Should().BeFalse();
                Guid.TryParse(value, out _).Should().BeFalse();
                IPAddress.TryParse(value, out _).Should().BeFalse();
            }
        }
    }

    private static AuthSecurityEvent SignInFailed() => new()
    {
        Definition = AuthSecurityEvents.SignInFailed,
        Outcome = AuthSecurityOutcome.Denied,
        Method = AuthSecurityMethod.Password,
    };

    private static DefaultHttpContext CreateContext(int? userId = null, string? sessionId = null, string ip = "192.0.2.10")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is not null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.Value.ToString()) };
            if (sessionId is not null)
                claims.Add(new Claim(TokenService.SessionIdClaimType, sessionId));
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        return context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
