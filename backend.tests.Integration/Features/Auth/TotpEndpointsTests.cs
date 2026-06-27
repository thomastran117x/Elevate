using System.Net;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

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
        var verifyBody = await app.ReadApiResponseAsync<MfaStatusResponse>(verify);
        verifyBody.Data!.IsTotpMfaEnabled.Should().BeTrue();
        verifyBody.Data.TotpEnrolledAtUtc.Should().NotBeNull();

        var status = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await app.ReadApiResponseAsync<MfaStatusResponse>(status);
        statusBody.Data!.IsTotpMfaEnabled.Should().BeTrue();
        statusBody.Data.TotpEnrollmentAvailable.Should().BeTrue();
        statusBody.Data.TotpEnrolledAtUtc.Should().NotBeNull();
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
        var disableBody = await app.ReadApiResponseAsync<MfaStatusResponse>(disable);
        disableBody.Data!.IsTotpMfaEnabled.Should().BeFalse();

        var status = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);
        var statusBody = await app.ReadApiResponseAsync<MfaStatusResponse>(status);
        statusBody.Data!.IsTotpMfaEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task TotpStepUpEndpoints_ShouldOfferTotp_AndCompleteLogin()
    {
        using var scope = new TemporaryEnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["AUTH_SMS_MFA_ENFORCEMENT_ENABLED"] = "true",
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
