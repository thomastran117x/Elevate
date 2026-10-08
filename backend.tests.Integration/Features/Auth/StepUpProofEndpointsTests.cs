using System.Net;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.profile.contracts.requests;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using OtpNet;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// End-to-end coverage for the 10-minute, session-bound step-up proof that guards every
/// sensitive account mutation.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class StepUpProofEndpointsTests
{
    public static TheoryData<string, string> SensitiveEndpoints => new()
    {
        { "PATCH", "/api/profile/username" },
        { "POST", "/api/profile/change-password" },
        { "POST", "/api/profile/email" },
        { "DELETE", "/api/profile" },
        { "POST", "/api/auth/mfa/enroll/start" },
        { "POST", "/api/auth/mfa/sms/enroll/start" },
        { "POST", "/api/auth/mfa/enable/start" },
        { "POST", "/api/auth/mfa/sms/enable/start" },
        { "POST", "/api/auth/mfa/enroll/verify" },
        { "POST", "/api/auth/mfa/sms/enroll/verify" },
        { "POST", "/api/auth/mfa/disable" },
        { "POST", "/api/auth/mfa/sms/disable" },
        { "POST", "/api/auth/mfa/remove" },
        { "POST", "/api/auth/mfa/sms/remove" },
        { "POST", "/api/auth/mfa/totp/enroll/start" },
        { "POST", "/api/auth/mfa/totp/enroll/verify" },
        { "POST", "/api/auth/mfa/totp/enable" },
        { "POST", "/api/auth/mfa/totp/disable" },
        { "POST", "/api/auth/mfa/totp/remove" },
    };

    [Theory]
    [MemberData(nameof(SensitiveEndpoints))]
    public async Task SensitiveEndpoint_ShouldRequireProof_BeforeAndAfterTheWindow_AndAcceptItDuring(
        string method,
        string path)
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var email = $"gate-{Guid.NewGuid():N}"[..20] + "@example.com";
        var session = await app.SignUpAndVerifyByTokenAsync(email, transport: SessionTransportResolver.ApiValue);
        var httpMethod = new HttpMethod(method);
        var payload = PayloadFor(path);

        var before = await app.SendWithBearerAndCsrfAsync(httpMethod, path, payload, session.AccessToken);
        await AssertMfaRequiredAsync(before);

        // A proof verified 10 minutes and a second ago is past its window even though the
        // Redis key still exists.
        await app.GrantStepUpProofAsync(session.AccessToken, DateTime.UtcNow.AddMinutes(-10).AddSeconds(-1));
        var after = await app.SendWithBearerAndCsrfAsync(httpMethod, path, payload, session.AccessToken);
        await AssertMfaRequiredAsync(after);

        await app.GrantStepUpProofAsync(session.AccessToken, DateTime.UtcNow.AddMinutes(-9));
        var during = await app.SendWithBearerAndCsrfAsync(httpMethod, path, payload, session.AccessToken);
        (await IsMfaRequiredAsync(during)).Should().BeFalse(
            $"{method} {path} should pass the gate inside the window (got {(int)during.StatusCode})");
    }

    [Fact]
    public async Task EmailStepUp_ShouldWriteBoundProof_ThatExpiresWithoutSliding()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("proof-shape@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("proof-shape@example.com");

        await app.CompleteSessionMfaByEmailAsync("proof-shape@example.com", session.AccessToken);

        var proof = await app.ReadStepUpProofAsync(session.AccessToken);
        proof.Should().NotBeNull();
        proof!.UserId.Should().Be(user!.Id);
        proof.AuthVersion.Should().Be(user.AuthVersion);
        proof.Method.Should().Be("email");
        proof.VerifiedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var key = backend.main.features.auth.mfa.session.StepUpProofKeys.ForSession(proof.SessionId);
        var ttlBefore = await app.Cache.GetTTLAsync(key);
        ttlBefore.Should().NotBeNull();
        ttlBefore!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));

        // Using gated routes must not extend the window.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        (await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var ttlAfter = await app.Cache.GetTTLAsync(key);
        ttlAfter!.Value.Should().BeLessThan(ttlBefore.Value);
        (await app.ReadStepUpProofAsync(session.AccessToken))!.VerifiedAtUtc.Should().Be(proof.VerifiedAtUtc);
    }

    [Fact]
    public async Task StepUpStartAndVerify_ShouldBeReachableWithoutAProof()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("no-self-block@example.com", transport: SessionTransportResolver.ApiValue);

        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/options", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/start",
            new SessionMfaStartRequest { Method = "email" },
            session.AccessToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var wrong = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/verify",
            new SessionMfaVerifyRequest { Method = "email", Code = "000000" },
            session.AccessToken);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IsMfaRequiredAsync(wrong)).Should().BeFalse();
    }

    [Fact]
    public async Task Proof_ShouldNotCarryOverToAnotherSession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var first = await app.SignUpAndVerifyByTokenAsync("proof-session@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("proof-session@example.com");
        await app.SeedKnownDeviceAsync(user!.Id, "known-device");
        var second = await app.LoginApiAsync("proof-session");

        await app.CompleteSessionMfaByEmailAsync("proof-session@example.com", first.AccessToken);

        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", first.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertMfaRequiredAsync(await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", second.AccessToken));
    }

    [Fact]
    public async Task Proof_ShouldBeRejected_WhenItsAuthVersionNoLongerMatches()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("proof-version@example.com", transport: SessionTransportResolver.ApiValue);
        await app.CompleteSessionMfaByEmailAsync("proof-version@example.com", session.AccessToken);

        // Simulate a proof minted before a credential rotation: same session, older version.
        var proof = (await app.ReadStepUpProofAsync(session.AccessToken))!;
        proof.AuthVersion -= 1;
        await app.Cache.SetValueAsync(
            backend.main.features.auth.mfa.session.StepUpProofKeys.ForSession(proof.SessionId),
            Newtonsoft.Json.JsonConvert.SerializeObject(proof),
            TimeSpan.FromMinutes(10));

        await AssertMfaRequiredAsync(await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken));
        (await app.ReadStepUpProofAsync(session.AccessToken)).Should().BeNull();
    }

    [Fact]
    public async Task Logout_ShouldDeleteTheSessionsProof()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("proof-logout@example.com", transport: SessionTransportResolver.ApiValue);
        await app.CompleteSessionMfaByEmailAsync("proof-logout@example.com", session.AccessToken);
        (await app.ReadStepUpProofAsync(session.AccessToken)).Should().NotBeNull();

        var logout = await app.Client.PostAsJsonAsync("/api/auth/api/logout", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.ReadStepUpProofAsync(session.AccessToken)).Should().BeNull();
    }

    [Fact]
    public async Task PasswordChange_ShouldLeaveNoUsableProof()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("proof-password@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "known-device");
        var session = await app.LoginApiAsync("proof-password");
        await app.CompleteSessionMfaByEmailAsync("proof-password@example.com", session.AccessToken);

        var change = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/profile/change-password",
            new ChangePasswordAuthenticatedRequest
            {
                CurrentPassword = "Password123!",
                NewPassword = "NewPassword456!"
            },
            session.AccessToken);
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.ReadStepUpProofAsync(session.AccessToken)).Should().BeNull();
        var fresh = await app.LoginApiAsync("proof-password", "NewPassword456!");
        await AssertMfaRequiredAsync(await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", fresh.AccessToken));
    }

    [Fact]
    public async Task FactorChange_ShouldSpendTheProofOnEverySession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var first = await app.SignUpAndVerifyByTokenAsync("proof-factor@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("proof-factor@example.com");
        await app.SeedKnownDeviceAsync(user!.Id, "known-device");
        var second = await app.LoginApiAsync("proof-factor");
        await app.GrantStepUpProofAsync(first.AccessToken);
        await app.GrantStepUpProofAsync(second.AccessToken);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/start",
            new { },
            first.AccessToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        // Starting enrollment changes nothing yet, so the proof is still available.
        (await app.ReadStepUpProofAsync(first.AccessToken)).Should().NotBeNull();
        var secret = (await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start)).Data!.SecretKey;

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest { Code = ComputeTotp(secret) },
            first.AccessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.ReadStepUpProofAsync(first.AccessToken)).Should().BeNull();
        (await app.ReadStepUpProofAsync(second.AccessToken)).Should().BeNull();
        await AssertMfaRequiredAsync(await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/disable",
            new TotpDisableRequest { Code = ComputeTotp(secret) },
            first.AccessToken));
    }

    [Fact]
    public async Task TotpStepUp_ShouldSatisfyTheGate()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("proof-totp@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("proof-totp@example.com");
        var secret = await EnrollTotpAsync(app, session.AccessToken);
        await app.Cache.DeleteKeyAsync($"totp:lastused:{user!.Id}");

        var options = await app.GetWithBearerAsync("/api/auth/mfa/step-up/options", session.AccessToken);
        (await app.ReadApiResponseAsync<SessionMfaOptionsResponse>(options)).Data!.AvailableMethods
            .Should().Contain(new[] { "totp", "email" });

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/verify",
            new SessionMfaVerifyRequest { Method = "totp", Code = ComputeTotp(secret) },
            session.AccessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.ReadStepUpProofAsync(session.AccessToken))!.Method.Should().Be("totp");
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SmsStepUp_ShouldSatisfyTheGate()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("proof-sms@example.com", transport: SessionTransportResolver.ApiValue);
        await EnrollSmsAsync(app, session.AccessToken);

        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/start",
            new SessionMfaStartRequest { Method = "sms" },
            session.AccessToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var sms = await app.WaitForSmsAsync(message => message.Purpose == "security verification");

        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/verify",
            new SessionMfaVerifyRequest { Method = "sms", Code = sms.Code },
            session.AccessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        (await app.ReadStepUpProofAsync(session.AccessToken))!.Method.Should().Be("sms");
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OAuthOnlyAccount_ShouldStepUpByEmail()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        app.OAuth.RegisterGoogleToken(
            "google-step-up-token",
            new OAuthUser("google-step-up-1", "oauth.stepup@example.com", "OAuth Step Up", "google"));

        var pending = await app.PostJsonWithCsrfAsync("/api/auth/google", new GoogleRequest
        {
            Token = "google-step-up-token",
            Transport = SessionTransportResolver.ApiValue
        });
        var signupToken = (await app.ReadApiResponseAsync<OAuthAuthenticationResponse>(pending)).Data!.SignupToken!;
        var complete = await app.PostJsonWithCsrfAsync("/api/auth/oauth/complete", new CompleteOAuthSignupRequest
        {
            SignupToken = signupToken,
            Usertype = "Participant",
            Username = "oauth.stepup",
            Transport = SessionTransportResolver.ApiValue
        });
        var session = (await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(complete)).Data!;
        (await app.QueryDbAsync(db => db.Users.SingleAsync(u => u.Email == "oauth.stepup@example.com")))
            .Password.Should().BeNull();

        var options = await app.GetWithBearerAsync("/api/auth/mfa/step-up/options", session.AccessToken);
        (await app.ReadApiResponseAsync<SessionMfaOptionsResponse>(options)).Data!.AvailableMethods
            .Should().Equal("email");
        await AssertMfaRequiredAsync(await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken));

        await app.CompleteSessionMfaByEmailAsync("oauth.stepup@example.com", session.AccessToken);

        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SeedBypass_ShouldNotExemptOrdinaryAccounts()
    {
        await using var app = await AuthApiTestApp.CreateAsync(
            configurationOverrides: new Dictionary<string, string?> { ["Auth:SeedAccountBypass"] = "true" });
        var session = await app.SignUpAndVerifyByTokenAsync("proof-bypass@example.com", transport: SessionTransportResolver.ApiValue);

        await AssertMfaRequiredAsync(await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken));
    }

    private static object? PayloadFor(string path) => path switch
    {
        "/api/profile/username" => new ChangeUsernameRequest { Username = "renamed-user" },
        "/api/profile/change-password" => new ChangePasswordAuthenticatedRequest
        {
            CurrentPassword = "Password123!",
            NewPassword = "NewPassword456!"
        },
        "/api/profile/email" => new ChangeEmailRequest
        {
            NewEmail = "moved@example.com",
            CurrentPassword = "Password123!"
        },
        "/api/profile" => null,
        _ when path.EndsWith("/enroll/start", StringComparison.Ordinal) && !path.Contains("/totp/") =>
            new MfaEnrollmentStartRequest { PhoneNumber = "+14165550123" },
        _ when path.EndsWith("/enroll/verify", StringComparison.Ordinal) && !path.Contains("/totp/") =>
            new MfaEnrollmentVerifyRequest { Code = "123456", Challenge = "missing-challenge" },
        _ when path.Contains("/totp/") && !path.EndsWith("/enroll/start", StringComparison.Ordinal) =>
            new TotpDisableRequest { Code = "123456" },
        _ => new { },
    };

    private static async Task AssertMfaRequiredAsync(HttpResponseMessage response)
    {
        (await IsMfaRequiredAsync(response)).Should().BeTrue(
            $"expected 403 MFA_REQUIRED but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<bool> IsMfaRequiredAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Forbidden
        && (await response.Content.ReadAsStringAsync()).Contains("MFA_REQUIRED", StringComparison.Ordinal);

    private static async Task<string> EnrollTotpAsync(AuthApiTestApp app, string accessToken)
    {
        await app.GrantStepUpProofAsync(accessToken);
        var start = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/mfa/totp/enroll/start", new { }, accessToken);
        var secret = (await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start)).Data!.SecretKey;
        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest { Code = ComputeTotp(secret) },
            accessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        return secret;
    }

    private static async Task EnrollSmsAsync(AuthApiTestApp app, string accessToken)
    {
        await app.GrantStepUpProofAsync(accessToken);
        var start = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/enroll/start",
            new MfaEnrollmentStartRequest { PhoneNumber = "+14165550123" },
            accessToken);
        var challenge = (await app.ReadApiResponseAsync<MfaChallengeResponse>(start)).Data!.Challenge;
        var sms = await app.WaitForSmsAsync(message => message.Challenge == challenge);
        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/enroll/verify",
            new MfaEnrollmentVerifyRequest { Challenge = challenge, Code = sms.Code },
            accessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ComputeTotp(string secret) =>
        new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
}
