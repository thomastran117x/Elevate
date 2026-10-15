using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using OtpNet;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Drives representative auth journeys through the real pipeline and inspects every security event
/// and metric they produce: the right event ids, and nothing a caller supplied as a secret.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthSecurityEventsTests
{
    private const string ClientAddress = "203.0.113.50";

    [Fact]
    public async Task Login_ShouldRecordFailureAndSuccess_WithoutCapturingCredentials()
    {
        var sink = new RecordingSink();
        await using var app = await CreateAppAsync(sink);
        using var metrics = new MetricCapture(app.Services.GetRequiredService<IMeterFactory>());
        var user = await app.SeedUserAsync("events-login@example.com", username: "events-login");
        await app.SeedKnownDeviceAsync(user.Id, "known-device");
        const string wrongPassword = "certainly not the right passphrase";

        var badLogin = await LoginAsync(app, "events-login", wrongPassword);
        badLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var goodLogin = await LoginAsync(app, "events-login", AuthApiTestApp.DefaultPassword);
        goodLogin.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(goodLogin));
        var session = (await app.ReadApiResponseAsync<LoginAuthenticationResponse>(goodLogin)).Data!.Auth!;

        var failed = sink.Single(AuthSecurityEvents.SignInFailed);
        failed.Reason.Should().Be(AuthSecurityReason.InvalidCredentials);
        failed.TargetHash.Should().MatchRegex("^[0-9a-f]{32}$");
        failed.SourceIpHash.Should().NotBeNull();
        failed.CorrelationId.Should().NotBeNullOrWhiteSpace();
        failed.ClientClass.Should().Be(AuthClientClass.Browser);

        var succeeded = sink.Single(AuthSecurityEvents.SignInSucceeded);
        succeeded.UserId.Should().Be(user.Id);
        succeeded.Method.Should().Be(AuthSecurityMethod.Password);
        succeeded.SessionId.Should().Be(ReadSessionId(session.AccessToken));

        sink.AssertCapturedNone(
            wrongPassword,
            AuthApiTestApp.DefaultPassword,
            "events-login",
            "events-login@example.com",
            session.AccessToken,
            session.RefreshToken!,
            session.SessionBindingToken!,
            "known-device",
            ClientAddress);
        metrics.AssertBoundedTags();
        metrics.Measurements.Should().Contain(measurement =>
            measurement.Instrument == "auth.operation.duration"
            && Equals(measurement.Tags["operation"], "sign_in")
            && Equals(measurement.Tags["outcome"], "denied"));
    }

    [Fact]
    public async Task AccountThrottle_ShouldRecordAThrottleWithTheTargetHashed()
    {
        var sink = new RecordingSink();
        await using var app = await CreateAppAsync(
            sink,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Auth:AbuseProtection:LoginAccountFailureLimit"] = "2",
                ["Auth:AbuseProtection:CaptchaFailureThreshold"] = "2",
                ["Auth:AbuseProtection:DelayBase"] = "00:00:00.001",
                ["Auth:AbuseProtection:DelayMaximum"] = "00:00:00.001",
            });
        await app.SeedUserAsync("events-throttle@example.com", username: "events-throttle");

        (await LoginAsync(app, "events-throttle", "wrong passphrase one")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(app, "events-throttle", "wrong passphrase two")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        var throttled = await LoginAsync(app, "events-throttle", "wrong passphrase three");

        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var throttle = sink.Single(AuthSecurityEvents.Throttled);
        throttle.Outcome.Should().Be(AuthSecurityOutcome.Throttled);
        throttle.Flow.Should().Be(AuthSecurityFlow.Login);
        throttle.Dimension.Should().Be(AuthThrottleDimension.Account);
        throttle.TargetHash.Should().Be(sink.Of(AuthSecurityEvents.SignInFailed).First().TargetHash);
        sink.AssertCapturedNone("events-throttle", "wrong passphrase", ClientAddress);
    }

    [Fact]
    public async Task RefreshReplay_ShouldRecordReuseAndTheRevocationItCaused()
    {
        var sink = new RecordingSink();
        var clock = new AdvanceableTimeProvider();
        await using var app = await CreateAppAsync(sink, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var first = await app.SignUpAndVerifyByTokenAsync(
            "events-replay@example.com",
            transport: SessionTransportResolver.ApiValue);
        var newest = await RefreshAsync(app, first);
        var sessionId = ReadSessionId(newest.AccessToken);

        clock.Advance(TimeSpan.FromSeconds(11));
        var replay = await PostRefreshAsync(app, first);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var reused = sink.Single(AuthSecurityEvents.RefreshTokenReused);
        reused.Reason.Should().Be(AuthSecurityReason.Replayed);
        reused.SessionId.Should().Be(sessionId);
        var revoked = sink.Single(AuthSecurityEvents.SessionRevoked);
        revoked.Reason.Should().Be(AuthSecurityReason.TokenReuse);
        revoked.SessionId.Should().Be(sessionId);
        revoked.UserId.Should().Be(reused.UserId);
        sink.AssertCapturedNone(
            first.RefreshToken!,
            first.SessionBindingToken!,
            newest.RefreshToken!,
            newest.AccessToken,
            "events-replay@example.com");
    }

    [Fact]
    public async Task TotpEnrollment_ShouldRecordTheFactorChange_WithoutTheSecretOrCode()
    {
        var sink = new RecordingSink();
        await using var app = await CreateAppAsync(sink);
        var session = await app.SignUpAndVerifyByTokenAsync(
            "events-totp@example.com",
            transport: SessionTransportResolver.ApiValue);
        await app.GrantStepUpProofAsync(session.AccessToken);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            session.AccessToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var secret = (await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start)).Data!.SecretKey;
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest { Code = code },
            session.AccessToken);

        verify.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(verify));
        var changed = sink.Single(AuthSecurityEvents.FactorChanged);
        changed.Factor.Should().Be(AuthSecurityFactor.Totp);
        changed.Change.Should().Be(AuthFactorChange.Enabled);
        changed.UserId.Should().NotBeNull();
        changed.SessionId.Should().Be(ReadSessionId(session.AccessToken));
        sink.AssertCapturedNone(secret, code, session.AccessToken, "events-totp@example.com");
    }

    [Fact]
    public async Task StepUpGate_ShouldRecordAnAuthorizationDenialWithItsReason()
    {
        var sink = new RecordingSink();
        await using var app = await CreateAppAsync(sink);
        var session = await app.SignUpAndVerifyByTokenAsync(
            "events-gate@example.com",
            transport: SessionTransportResolver.ApiValue);

        var gated = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);

        gated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var denied = sink.Single(AuthSecurityEvents.AuthorizationDenied);
        denied.Reason.Should().Be(AuthSecurityReason.MfaRequired);
        denied.Route.Should().StartWith("GET ").And.Contain("auth/mfa");
        denied.UserId.Should().NotBeNull();
        denied.SessionId.Should().Be(ReadSessionId(session.AccessToken));
        sink.AssertCapturedNone(session.AccessToken, "events-gate@example.com");
    }

    private static Task<AuthApiTestApp> CreateAppAsync(
        RecordingSink sink,
        Action<IServiceCollection>? serviceOverrides = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null) =>
        AuthApiTestApp.CreateAsync(
            serviceOverrides: services =>
            {
                services.AddSingleton<IAuthSecurityEventSink>(sink);
                serviceOverrides?.Invoke(services);
            },
            configurationOverrides: configurationOverrides);

    private static async Task<HttpResponseMessage> LoginAsync(AuthApiTestApp app, string username, string password)
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
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "known-device");
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostRefreshAsync(
        AuthApiTestApp app,
        AuthenticatedSessionResponse session) =>
        app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });

    private static async Task<AuthenticatedSessionResponse> RefreshAsync(
        AuthApiTestApp app,
        AuthenticatedSessionResponse session)
    {
        var response = await PostRefreshAsync(app, session);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(response)).Data!;
    }

    private static string ReadSessionId(string accessToken) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .First(claim => claim.Type == TokenService.SessionIdClaimType)
            .Value;

    private sealed class RecordingSink : IAuthSecurityEventSink
    {
        private readonly ConcurrentQueue<AuthSecurityEvent> _events = new();

        public void Write(AuthSecurityEvent securityEvent) => _events.Enqueue(securityEvent);

        public IEnumerable<AuthSecurityEvent> Of(AuthSecurityEventDefinition definition) =>
            _events.Where(securityEvent => securityEvent.Definition == definition);

        public AuthSecurityEvent Single(AuthSecurityEventDefinition definition) =>
            Of(definition).Should().ContainSingle($"one {definition.Name} event is expected").Subject;

        /// <summary>
        /// Fails if any recorded event, serialized whole, contains one of the values: the test's own
        /// passwords, tokens, codes, and contact details.
        /// </summary>
        public void AssertCapturedNone(params string[] prohibited)
        {
            var serialized = JsonSerializer.Serialize(_events.ToArray());
            _events.Should().NotBeEmpty();
            foreach (var value in prohibited)
                serialized.Should().NotContainEquivalentOf(value);
        }
    }

    /// <summary>Collects measurements from this app's own meters only.</summary>
    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture(IMeterFactory meterFactory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AuthSecurityMetrics.MeterName
                    && ReferenceEquals(instrument.Meter.Scope, meterFactory))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Add(instrument, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Add(instrument, tags));
            _listener.Start();
        }

        public ConcurrentQueue<(string Instrument, IReadOnlyDictionary<string, object?> Tags)> Measurements { get; } = new();

        public void AssertBoundedTags()
        {
            Measurements.Should().NotBeEmpty();
            string[] allowedKeys = ["event", "outcome", "reason", "method", "flow", "dimension", "factor", "operation", "sink"];
            foreach (var (_, tags) in Measurements)
            {
                tags.Keys.Should().BeSubsetOf(allowedKeys);
                tags.Values.Should().OnlyContain(value => value is string
                    && System.Text.RegularExpressions.Regex.IsMatch((string)value, "^[a-z_.]+$"));
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
                copy[tag.Key] = tag.Value;
            Measurements.Enqueue((instrument.Name, copy));
        }
    }

    /// <summary>Wall-clock time plus an offset the test can move forward.</summary>
    private sealed class AdvanceableTimeProvider : TimeProvider
    {
        private TimeSpan _offset;

        public void Advance(TimeSpan by) => _offset += by;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;
    }
}
