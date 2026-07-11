using System.Net;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.token;
using backend.main.infrastructure.database.core;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using OtpNet;

namespace backend.tests.Integration.Features.Auth;

public class TotpEndpointsTests
{
    [Fact]
    public async Task TotpEnrollmentEndpoints_ShouldStartVerify_AndBeVisibleInStatus()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "totp-enroll@example.com",
            transport: SessionTransportResolver.ApiValue);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            session.AccessToken);

        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var startBody = await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start);
        startBody.Data!.SecretKey.Should().NotBeNullOrWhiteSpace();
        startBody.Data.QrCodeUri.Should().Contain(startBody.Data.SecretKey);
        startBody.Data.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest
            {
                Code = ComputeCode(startBody.Data.SecretKey)
            },
            session.AccessToken);

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(verify);
        verifyBody.Data!.Totp.IsEnabled.Should().BeTrue();
        verifyBody.Data.Totp.EnrolledAtUtc.Should().NotBeNull();

        var persistedEnrollment = await app.QueryDbAsync(db => db.TotpMfaEnrollments.SingleOrDefaultAsync());
        persistedEnrollment.Should().NotBeNull();
        persistedEnrollment!.IsTotpMfaEnabled.Should().BeTrue();
        persistedEnrollment.EnrolledAtUtc.Should().NotBeNull();

        await app.CompleteSessionMfaByEmailAsync("totp-enroll@example.com", session.AccessToken);
        var status = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(status);
        statusBody.Data!.Totp.IsEnabled.Should().BeTrue();
        statusBody.Data.Totp.EnrollmentAvailable.Should().BeTrue();
        statusBody.Data.Totp.EnrolledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task TotpDisableEndpoint_ShouldDisableEnrollment_AndStatus()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "totp-disable@example.com",
            transport: SessionTransportResolver.ApiValue);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            session.AccessToken);
        var startBody = await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start);

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest
            {
                Code = ComputeCode(startBody.Data!.SecretKey)
            },
            session.AccessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await app.FindUserByEmailAsync("totp-disable@example.com");
        await app.Cache.DeleteKeyAsync($"totp:lastused:{user!.Id}");

        var disable = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/disable",
            new TotpDisableRequest
            {
                Code = ComputeCode(startBody.Data.SecretKey)
            },
            session.AccessToken);

        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        var disableBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(disable);
        disableBody.Data!.Totp.IsEnabled.Should().BeFalse();
        disableBody.Data.Totp.DisabledAtUtc.Should().NotBeNull();

        var persistedDisabled = await app.QueryDbAsync(db =>
            db.TotpMfaEnrollments.SingleAsync(e => e.UserId == user.Id));
        persistedDisabled.IsTotpMfaEnabled.Should().BeFalse();
        persistedDisabled.DisabledAtUtc.Should().NotBeNull();

        await app.CompleteSessionMfaByEmailAsync("totp-disable@example.com", session.AccessToken);
        var status = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);
        var statusBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(status);
        statusBody.Data!.Totp.IsEnabled.Should().BeFalse();
        statusBody.Data.Totp.DisabledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task TotpEnableAndRemoveEndpoints_ShouldReenableAndDeleteConfiguredMethod()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "totp-enable-remove@example.com",
            transport: SessionTransportResolver.ApiValue);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            session.AccessToken);
        var startBody = await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start);

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest
            {
                Code = ComputeCode(startBody.Data!.SecretKey)
            },
            session.AccessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await app.FindUserByEmailAsync("totp-enable-remove@example.com");
        await app.Cache.DeleteKeyAsync($"totp:lastused:{user!.Id}");

        var disable = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/disable",
            new TotpDisableRequest
            {
                Code = ComputeCode(startBody.Data.SecretKey)
            },
            session.AccessToken);
        disable.StatusCode.Should().Be(HttpStatusCode.OK);

        await app.Cache.DeleteKeyAsync($"totp:lastused:{user.Id}");
        var enable = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enable",
            new TotpDisableRequest
            {
                Code = ComputeCode(startBody.Data.SecretKey)
            },
            session.AccessToken);

        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        var enableBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(enable);
        enableBody.Data!.Totp.IsEnabled.Should().BeTrue();
        enableBody.Data.Totp.DisabledAtUtc.Should().BeNull();

        var persistedReenabled = await app.QueryDbAsync(db =>
            db.TotpMfaEnrollments.SingleAsync(e => e.UserId == user.Id));
        persistedReenabled.IsTotpMfaEnabled.Should().BeTrue();
        persistedReenabled.DisabledAtUtc.Should().BeNull();

        await app.Cache.DeleteKeyAsync($"totp:lastused:{user.Id}");
        var remove = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/remove",
            new TotpDisableRequest
            {
                Code = ComputeCode(startBody.Data.SecretKey)
            },
            session.AccessToken);

        remove.StatusCode.Should().Be(HttpStatusCode.OK);
        var removeBody = await app.ReadApiResponseAsync<MfaSettingsResponse>(remove);
        removeBody.Data!.Totp.IsConfigured.Should().BeFalse();
        removeBody.Data.Totp.IsEnabled.Should().BeFalse();
        removeBody.Data.Totp.EnrolledAtUtc.Should().BeNull();
        removeBody.Data.Totp.DisabledAtUtc.Should().BeNull();

        (await app.QueryDbAsync(db => db.TotpMfaEnrollments.AnyAsync(e => e.UserId == user.Id))).Should().BeFalse();
    }
    [Fact]
    public async Task TotpStepUpEndpoints_ShouldOfferTotp_AndCompleteLogin()
    {
        using var scope = new TemporaryEnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AUTH_SMS_MFA_ENFORCEMENT_ENABLED"] = "false",
            ["AUTH_TOTP_MFA_STEP_UP_ENABLED"] = "true"
        });

        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "totp-stepup@example.com",
            transport: SessionTransportResolver.ApiValue);

        var startEnrollment = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            session.AccessToken);
        var startBody = await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(startEnrollment);

        var verifyEnrollment = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest
            {
                Code = ComputeCode(startBody.Data!.SecretKey)
            },
            session.AccessToken);
        verifyEnrollment.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await app.FindUserByEmailAsync("totp-stepup@example.com");
        await app.Cache.DeleteKeyAsync($"totp:lastused:{user!.Id}");
        app.Publisher.Clear();

        var login = await app.PostJsonWithCsrfAsync("/api/auth/login", new LoginRequest
        {
            Email = "totp-stepup@example.com",
            Password = "Password123!",
            Captcha = "captcha",
            Transport = SessionTransportResolver.ApiValue,
            ReturnUrl = "/bookings/321"
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await app.ReadApiResponseAsync<LoginAuthenticationResponse>(login);
        loginBody.Data!.Type.Should().Be("requires_step_up");
        loginBody.Data.StepUp.Should().NotBeNull();
        loginBody.Data.StepUp!.AvailableMethods.Should().Contain("totp");
        loginBody.Data.StepUp.AvailableMethods.Should().Contain("email");
        loginBody.Data.StepUp.AvailableMethods.Should().NotContain("sms");

        var startStepUp = await app.PostJsonWithCsrfAsync("/api/auth/mfa/start", new StartLoginStepUpRequest
        {
            Challenge = loginBody.Data.StepUp.Challenge,
            Method = "totp"
        });

        startStepUp.StatusCode.Should().Be(HttpStatusCode.OK);
        var startStepUpBody = await app.ReadApiResponseAsync<StartLoginStepUpResponse>(startStepUp);
        startStepUpBody.Data!.SelectedMethod.Should().Be("totp");
        startStepUpBody.Data.Challenge.Should().Be(loginBody.Data.StepUp.Challenge);
        startStepUpBody.Data.MaskedDestination.Should().Be("authenticator app");
        app.Publisher.SmsMessages.Should().BeEmpty();
        app.Publisher.EmailMessages.Should().BeEmpty();

        var verifyStepUp = await app.PostJsonWithCsrfAsync("/api/auth/mfa/verify/totp", new VerifyLoginStepUpRequest
        {
            Challenge = startStepUpBody.Data.Challenge,
            Code = ComputeCode(startBody.Data.SecretKey)
        });

        verifyStepUp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyStepUpBody = await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(verifyStepUp);
        verifyStepUpBody.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        verifyStepUpBody.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        verifyStepUpBody.Data.SessionBindingToken.Should().NotBeNullOrWhiteSpace();
        verifyStepUpBody.Data.ReturnPath.Should().Be("/bookings/321");
    }

    private static string ComputeCode(string secret)
    {
        var secretBytes = Base32Encoding.ToBytes(secret);
        return new Totp(secretBytes).ComputeTotp();
    }

    private sealed class TemporaryEnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = [];

        public TemporaryEnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _originals[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}

