using System.Net;
using System.Net.Http.Json;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.shared.responses;
using backend.main.shared.providers.messages;
using backend.main.utilities;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Features.Auth;

public class AuthEndpointsTests
{
    [Fact]
    public async Task Signup_AndVerifyByToken_ShouldCreateAuthenticatedSession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var session = await app.SignUpAndVerifyByTokenAsync("verify-token@example.com");
        var createdUser = await app.FindUserByEmailAsync("verify-token@example.com");

        session.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.RefreshToken.Should().BeNull();
        createdUser.Should().NotBeNull();
        createdUser!.Usertype.Should().Be("Participant");
    }

    [Fact]
    public async Task Signup_AndVerifyByOtp_ShouldCreateAuthenticatedSession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var signup = await app.PostJsonWithCsrfAsync("/api/auth/signup", new SignUpRequest
        {
            Email = "verify-otp@example.com",
            Password = "Password123!",
            Usertype = "Organizer",
            Captcha = "captcha"
        });
        signup.StatusCode.Should().Be(HttpStatusCode.OK);

        var signupBody = await app.ReadApiResponseAsync<VerificationChallengeResponse>(signup);
        var message = app.Publisher.EmailMessages.Last(email =>
            email.Type == EmailMessageType.VerifyEmail && email.Email == "verify-otp@example.com");

        var verify = await app.PostJsonWithCsrfAsync("/api/auth/verify/otp", new OtpVerificationRequest
        {
            Challenge = signupBody.Data!.Challenge,
            Code = message.Code!,
        });

        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(verify);
        verifyBody.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        (await app.FindUserByEmailAsync("verify-otp@example.com"))!.Usertype.Should().Be("Organizer");
    }

    [Fact]
    public async Task Login_ShouldSucceedForKnownDevice_AndRejectInvalidCredentials()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("login@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "known-device");

        var badLogin = await app.PostJsonWithCsrfAsync("/api/auth/login", new LoginRequest
        {
            Email = "login@example.com",
            Password = "WrongPassword123!",
            Captcha = "captcha"
        });

        badLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Email = "login@example.com",
                Password = "Password123!",
                Captcha = "captcha"
            })
        };
        loginRequest.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "known-device");
        loginRequest.Headers.Add(backend.main.application.security.CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());

        var login = await app.Client.SendAsync(loginRequest);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(login);
        loginBody.Data!.RefreshToken.Should().BeNull();
        AuthApiTestApp.ExtractCookie(login, HttpUtility.RefreshCookieName).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturnChallengeForExistingUsers_AndPlaceholderForUnknownUsers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        await app.SeedUserAsync("forgot@example.com");

        var existing = await app.PostJsonWithCsrfAsync("/api/auth/forgot-password", new ForgotPasswordRequest
        {
            Email = "forgot@example.com",
            Captcha = "captcha"
        });
        existing.StatusCode.Should().Be(HttpStatusCode.OK);
        app.Publisher.EmailMessages.Should().ContainSingle(message =>
            message.Type == EmailMessageType.ResetPassword && message.Email == "forgot@example.com");

        app.Publisher.Clear();

        var missing = await app.PostJsonWithCsrfAsync("/api/auth/forgot-password", new ForgotPasswordRequest
        {
            Email = "missing@example.com",
            Captcha = "captcha"
        });
        missing.StatusCode.Should().Be(HttpStatusCode.OK);
        var missingBody = await app.ReadApiResponseAsync<VerificationChallengeResponse>(missing);
        missingBody.Data!.Challenge.Should().NotBeNullOrWhiteSpace();
        app.Publisher.EmailMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_ShouldSupportTokenAndOtpFlows()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("reset@example.com", "Password123!");
        await app.SeedKnownDeviceAsync(user.Id, "trusted-reset-device");

        var forgotByToken = await app.PostJsonWithCsrfAsync("/api/auth/forgot-password", new ForgotPasswordRequest
        {
            Email = "reset@example.com",
            Captcha = "captcha"
        });
        forgotByToken.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.ResetPassword && message.Email == "reset@example.com");

        var resetByToken = await app.PostJsonWithCsrfAsync(
            $"/api/auth/change-password?token={Uri.EscapeDataString(tokenEmail.Token)}",
            new ChangePasswordRequest
            {
                Password = "NewPassword123!"
            });
        resetByToken.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginAfterTokenReset = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Email = "reset@example.com",
                Password = "NewPassword123!",
                Captcha = "captcha"
            })
        };
        loginAfterTokenReset.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "trusted-reset-device");
        loginAfterTokenReset.Headers.Add(backend.main.application.security.CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());

        var loginTokenFlow = await app.Client.SendAsync(loginAfterTokenReset);
        loginTokenFlow.StatusCode.Should().Be(HttpStatusCode.OK);

        app.Publisher.Clear();

        var forgotByOtp = await app.PostJsonWithCsrfAsync("/api/auth/forgot-password", new ForgotPasswordRequest
        {
            Email = "reset@example.com",
            Captcha = "captcha"
        });
        var forgotBody = await app.ReadApiResponseAsync<VerificationChallengeResponse>(forgotByOtp);
        var otpEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.ResetPassword && message.Email == "reset@example.com");

        var resetByOtp = await app.PostJsonWithCsrfAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            Password = "OtpReset123!",
            Challenge = forgotBody.Data!.Challenge,
            Code = otpEmail.Code!
        });
        var otpResetBody = await resetByOtp.Content.ReadAsStringAsync();
        resetByOtp.StatusCode.Should().Be(HttpStatusCode.OK, otpResetBody);

        var loginAfterOtpReset = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Email = "reset@example.com",
                Password = "OtpReset123!",
                Captcha = "captcha"
            })
        };
        loginAfterOtpReset.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "trusted-reset-device");
        loginAfterOtpReset.Headers.Add(backend.main.application.security.CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());

        var otpLogin = await app.Client.SendAsync(loginAfterOtpReset);
        otpLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ShouldRotateCookies_AndRejectRefreshTokenReuse()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        await app.SignUpAndVerifyByTokenAsync("refresh@example.com");

        var browserRefreshA = await app.PostJsonWithCsrfAsync("/api/auth/refresh", new RefreshTokenRequest());
        browserRefreshA.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieRefreshA = AuthApiTestApp.ExtractCookie(browserRefreshA, HttpUtility.RefreshCookieName);
        var cookieBindingA = AuthApiTestApp.ExtractCookie(browserRefreshA, HttpUtility.RefreshBindingCookieName);

        var browserRefreshB = await app.PostJsonWithCsrfAsync("/api/auth/refresh", new RefreshTokenRequest());
        browserRefreshB.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieRefreshB = AuthApiTestApp.ExtractCookie(browserRefreshB, HttpUtility.RefreshCookieName);
        var cookieBindingB = AuthApiTestApp.ExtractCookie(browserRefreshB, HttpUtility.RefreshBindingCookieName);

        cookieRefreshA.Should().NotBeNullOrWhiteSpace();
        cookieBindingA.Should().NotBeNullOrWhiteSpace();
        cookieRefreshB.Should().NotBeNullOrWhiteSpace();
        cookieBindingB.Should().NotBeNullOrWhiteSpace();
        cookieRefreshB.Should().NotBe(cookieRefreshA);
        cookieBindingB.Should().NotBe(cookieBindingA);

        var apiSession = await app.SignUpAndVerifyByTokenAsync(
            "api-refresh@example.com",
            transport: SessionTransportResolver.ApiValue);
        apiSession.RefreshToken.Should().NotBeNullOrWhiteSpace();
        apiSession.SessionBindingToken.Should().NotBeNullOrWhiteSpace();

        var apiRefresh = await app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = apiSession.RefreshToken,
            SessionBindingToken = apiSession.SessionBindingToken
        });
        apiRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = apiSession.RefreshToken,
            SessionBindingToken = apiSession.SessionBindingToken
        });

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldRevokeActiveRefreshSession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var apiSession = await app.SignUpAndVerifyByTokenAsync(
            "logout@example.com",
            transport: SessionTransportResolver.ApiValue);

        var logout = await app.Client.PostAsJsonAsync("/api/auth/api/logout", new RefreshTokenRequest
        {
            RefreshToken = apiSession.RefreshToken,
            SessionBindingToken = apiSession.SessionBindingToken
        });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = apiSession.RefreshToken,
            SessionBindingToken = apiSession.SessionBindingToken
        });

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OAuthCompletion_ShouldSupportSuccess_TransportMismatch_AndExpiredState()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        app.OAuth.RegisterGoogleToken(
            "google-new-user",
            new OAuthUser("google-user-1", "google.new@example.com", "Google New", "google"));
        app.OAuth.RegisterGoogleToken(
            "google-expired-user",
            new OAuthUser("google-user-2", "google.expired@example.com", "Google Expired", "google"));

        var pending = await app.PostJsonWithCsrfAsync("/api/auth/google", new GoogleRequest
        {
            Token = "google-new-user"
        });
        pending.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingBody = await app.ReadApiResponseAsync<OAuthAuthenticationResponse>(pending);
        pendingBody.Data!.RequiresRoleSelection.Should().BeTrue();

        var mismatch = await app.PostJsonWithCsrfAsync("/api/auth/oauth/complete", new CompleteOAuthSignupRequest
        {
            SignupToken = pendingBody.Data.SignupToken!,
            Usertype = "Participant",
            Transport = SessionTransportResolver.ApiValue
        });
        mismatch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var linkedUser = await app.SeedUserAsync(
            "google.new@example.com",
            googleId: "google-user-1");
        await app.SeedKnownDeviceAsync(linkedUser.Id, "oauth-known-device");

        var successRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/oauth/complete")
        {
            Content = JsonContent.Create(new CompleteOAuthSignupRequest
            {
                SignupToken = pendingBody.Data.SignupToken!,
                Usertype = "Participant"
            })
        };
        successRequest.Headers.Add(HttpUtility.TrustedDeviceHeaderName, "oauth-known-device");
        successRequest.Headers.Add(
            backend.main.application.security.CsrfConfiguration.CsrfHeaderName,
            await app.GetCsrfTokenAsync());

        var success = await app.Client.SendAsync(successRequest);
        var successBody = await success.Content.ReadAsStringAsync();
        success.StatusCode.Should().Be(HttpStatusCode.OK, successBody);
        (await app.FindUserByEmailAsync("google.new@example.com")).Should().NotBeNull();

        var expiredPending = await app.PostJsonWithCsrfAsync("/api/auth/google", new GoogleRequest
        {
            Token = "google-expired-user"
        });
        var expiredBody = await app.ReadApiResponseAsync<OAuthAuthenticationResponse>(expiredPending);
        await app.DeletePendingOAuthSignupAsync(expiredBody.Data!.SignupToken!);

        var expired = await app.PostJsonWithCsrfAsync("/api/auth/oauth/complete", new CompleteOAuthSignupRequest
        {
            SignupToken = expiredBody.Data.SignupToken!,
            Usertype = "Organizer"
        });
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ShouldReturnCurrentUserForAuthenticatedAccessToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var session = await app.SignUpAndVerifyByTokenAsync("me@example.com", role: "Organizer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);

        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await app.ReadApiResponseAsync<CurrentUserResponse>(response);
        body.Data!.Email.Should().Be("me@example.com");
        body.Data.Usertype.Should().Be("Organizer");
    }

    [Fact]
    public async Task BrowserLogout_AndVerificationLinkEndpoints_ShouldSupportRedirectAndRevocation()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var signup = await app.PostJsonWithCsrfAsync("/api/auth/signup", new SignUpRequest
        {
            Email = "browser-logout@example.com",
            Password = "Password123!",
            Usertype = "Participant",
            Captcha = "captcha"
        });
        signup.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.VerifyEmail && message.Email == "browser-logout@example.com");

        var verifyRedirect = await app.Client.GetAsync($"/api/auth/verify?token={Uri.EscapeDataString(verifyEmail.Token)}");
        verifyRedirect.StatusCode.Should().Be(HttpStatusCode.Found);
        verifyRedirect.Headers.Location.Should().NotBeNull();
        verifyRedirect.Headers.Location!.ToString()
            .Should().Be($"http://localhost:3090/auth/verify?token={Uri.EscapeDataString(verifyEmail.Token)}");

        var verify = await app.PostJsonWithCsrfAsync("/api/auth/verify", new VerificationTokenRequest
        {
            Token = verifyEmail.Token
        });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await app.PostJsonWithCsrfAsync("/api/auth/refresh", new RefreshTokenRequest());
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await app.PostJsonWithCsrfAsync("/api/auth/logout", new RefreshTokenRequest());
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        (await logout.Content.ReadAsStringAsync()).Should().Contain("logout is successful");

        var replayRefresh = await app.PostJsonWithCsrfAsync("/api/auth/refresh", new RefreshTokenRequest());
        replayRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var secondLogout = await app.PostJsonWithCsrfAsync("/api/auth/logout", new RefreshTokenRequest());
        secondLogout.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondLogout.Content.ReadAsStringAsync()).Should().Contain("already logged out");
    }

    [Fact]
    public async Task DeviceVerificationEndpoints_ShouldRedirectAndAuthenticatePendingDevice()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        await app.SeedUserAsync("new-device@example.com");

        var login = await app.PostJsonWithCsrfAsync("/api/auth/login", new LoginRequest
        {
            Email = "new-device@example.com",
            Password = "Password123!",
            Captcha = "captcha",
            Transport = SessionTransportResolver.ApiValue
        });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.Content.ReadAsStringAsync()).Should().Contain("Device verification required");

        var deviceEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.NewDevice && message.Email == "new-device@example.com");

        var verifyRedirect = await app.Client.GetAsync($"/api/auth/device/verify?token={Uri.EscapeDataString(deviceEmail.Token)}");
        verifyRedirect.StatusCode.Should().Be(HttpStatusCode.Found);
        verifyRedirect.Headers.Location.Should().NotBeNull();
        verifyRedirect.Headers.Location!.ToString()
            .Should().Be($"http://localhost:3090/auth/device/verify?token={Uri.EscapeDataString(deviceEmail.Token)}");

        var verify = await app.PostJsonWithCsrfAsync("/api/auth/device/verify", new VerificationTokenRequest
        {
            Token = deviceEmail.Token,
            Transport = SessionTransportResolver.ApiValue
        });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(verify);
        verifyBody.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        verifyBody.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        verifyBody.Data.SessionBindingToken.Should().NotBeNullOrWhiteSpace();

        var refresh = await app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = verifyBody.Data.RefreshToken,
            SessionBindingToken = verifyBody.Data.SessionBindingToken
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OAuthCodeAndMicrosoftEndpoints_ShouldAuthenticateLinkedUsers()
    {
        await using var googleApp = await AuthApiTestApp.CreateAsync();

        googleApp.OAuth.RegisterGoogleToken(
            "google-code-token",
            new OAuthUser("google-linked-id", "google.code@example.com", "Google Code User", "google"));

        var googleUser = await googleApp.SeedUserAsync(
            "google.code@example.com",
            googleId: "google-linked-id");
        await googleApp.SeedKnownDeviceAsync(googleUser.Id, "google-code-device");

        var googleRequest = await CreateCsrfRequestAsync(
            googleApp,
            "/api/auth/google/code",
            new GoogleCodeRequest
            {
                Code = "google-code-token",
                CodeVerifier = "verifier",
                RedirectUri = "https://app.test/oauth/callback",
                Transport = SessionTransportResolver.ApiValue
            },
            trustedDeviceToken: "google-code-device");

        var googleResponse = await googleApp.Client.SendAsync(googleRequest);
        googleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var googleBody = await googleApp.ReadApiResponseAsync<OAuthAuthenticationResponse>(googleResponse);
        googleBody.Data!.RequiresRoleSelection.Should().BeFalse();
        googleBody.Data.Auth.Should().NotBeNull();
        googleBody.Data.Auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        googleBody.Data.Auth.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await using var microsoftApp = await AuthApiTestApp.CreateAsync();

        microsoftApp.OAuth.RegisterMicrosoftToken(
            "microsoft-token",
            new OAuthUser("microsoft-linked-id", "microsoft.user@example.com", "Microsoft User", "microsoft"));

        var microsoftUser = await microsoftApp.SeedUserAsync(
            "microsoft.user@example.com",
            microsoftId: "microsoft-linked-id");
        await microsoftApp.SeedKnownDeviceAsync(microsoftUser.Id, "microsoft-device");

        var microsoftRequest = await CreateCsrfRequestAsync(
            microsoftApp,
            "/api/auth/microsoft",
            new MicrosoftRequest
            {
                Token = "microsoft-token",
                Transport = SessionTransportResolver.ApiValue
            },
            trustedDeviceToken: "microsoft-device");

        var microsoftResponse = await microsoftApp.Client.SendAsync(microsoftRequest);
        microsoftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var microsoftBody = await microsoftApp.ReadApiResponseAsync<OAuthAuthenticationResponse>(microsoftResponse);
        microsoftBody.Data!.RequiresRoleSelection.Should().BeFalse();
        microsoftBody.Data.Auth.Should().NotBeNull();
        microsoftBody.Data.Auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        microsoftBody.Data.Auth.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }


[Fact]
public async Task MfaStatus_ShouldReturnEmptyState_ForAuthenticatedUser()
{
    await using var app = await AuthApiTestApp.CreateAsync();
    var session = await app.SignUpAndVerifyByTokenAsync("mfa-status@example.com", transport: SessionTransportResolver.ApiValue);

    var response = await app.GetWithBearerAsync("/api/auth/mfa", session.AccessToken);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await app.ReadApiResponseAsync<MfaStatusResponse>(response);
    body.Data!.EnrollmentAvailable.Should().BeTrue();
    body.Data.IsSmsMfaEnabled.Should().BeFalse();
    body.Data.MaskedPhoneNumber.Should().BeNull();
}

[Fact]
public async Task MfaEnrollmentStart_ShouldPublishSmsMessage()
{
    await using var app = await AuthApiTestApp.CreateAsync();
    var session = await app.SignUpAndVerifyByTokenAsync("mfa-start@example.com", transport: SessionTransportResolver.ApiValue);

    var response = await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/enroll/start",
        new MfaEnrollmentStartRequest
        {
            PhoneNumber = "+14165550123"
        },
        session.AccessToken);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await app.ReadApiResponseAsync<MfaChallengeResponse>(response);
    body.Data!.MaskedDestination.Should().Be("***-***-0123");
    app.Publisher.SmsMessages.Should().ContainSingle(message =>
        message.PhoneNumber == "+14165550123"
        && message.Challenge == body.Data.Challenge
        && message.Purpose == "mfa enrollment");
}

[Fact]
public async Task MfaEnrollmentVerify_ShouldPersistEnabledEnrollment()
{
    await using var app = await AuthApiTestApp.CreateAsync();
    var session = await app.SignUpAndVerifyByTokenAsync("mfa-verify@example.com", transport: SessionTransportResolver.ApiValue);

    var start = await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/enroll/start",
        new MfaEnrollmentStartRequest
        {
            PhoneNumber = "+14165550123"
        },
        session.AccessToken);
    var startBody = await app.ReadApiResponseAsync<MfaChallengeResponse>(start);
    var sms = app.Publisher.SmsMessages.Last(message => message.Challenge == startBody.Data!.Challenge);

    var verify = await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/enroll/verify",
        new MfaEnrollmentVerifyRequest
        {
            Challenge = startBody.Data!.Challenge,
            Code = sms.Code
        },
        session.AccessToken);

    verify.StatusCode.Should().Be(HttpStatusCode.OK);
    var verifyBody = await app.ReadApiResponseAsync<MfaStatusResponse>(verify);
    verifyBody.Data!.IsSmsMfaEnabled.Should().BeTrue();
    verifyBody.Data.MaskedPhoneNumber.Should().Be("***-***-0123");

    var user = await app.FindUserByEmailAsync("mfa-verify@example.com");
    var enrollment = await app.FindSmsMfaEnrollmentAsync(user!.Id);
    enrollment.Should().NotBeNull();
    enrollment!.PhoneNumber.Should().Be("+14165550123");
    enrollment.IsSmsMfaEnabled.Should().BeTrue();
    enrollment.PhoneVerifiedAtUtc.Should().NotBeNull();
}

[Fact]
public async Task MfaDisable_ShouldKeepPhoneButDisableEnrollment()
{
    await using var app = await AuthApiTestApp.CreateAsync();
    var session = await app.SignUpAndVerifyByTokenAsync("mfa-disable@example.com", transport: SessionTransportResolver.ApiValue);

    var start = await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/enroll/start",
        new MfaEnrollmentStartRequest
        {
            PhoneNumber = "+14165550123"
        },
        session.AccessToken);
    var startBody = await app.ReadApiResponseAsync<MfaChallengeResponse>(start);
    var sms = app.Publisher.SmsMessages.Last(message => message.Challenge == startBody.Data!.Challenge);

    await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/enroll/verify",
        new MfaEnrollmentVerifyRequest
        {
            Challenge = startBody.Data!.Challenge,
            Code = sms.Code
        },
        session.AccessToken);

    var disable = await app.PostJsonWithBearerAndCsrfAsync(
        "/api/auth/mfa/disable",
        new MfaDisableRequest(),
        session.AccessToken);

    disable.StatusCode.Should().Be(HttpStatusCode.OK);
    var disableBody = await app.ReadApiResponseAsync<MfaStatusResponse>(disable);
    disableBody.Data!.IsSmsMfaEnabled.Should().BeFalse();
    disableBody.Data.MaskedPhoneNumber.Should().Be("***-***-0123");

    var user = await app.FindUserByEmailAsync("mfa-disable@example.com");
    var enrollment = await app.FindSmsMfaEnrollmentAsync(user!.Id);
    enrollment.Should().NotBeNull();
    enrollment!.IsSmsMfaEnabled.Should().BeFalse();
    enrollment.PhoneNumber.Should().Be("+14165550123");
    enrollment.PhoneVerifiedAtUtc.Should().NotBeNull();
}

    private static async Task<HttpRequestMessage> CreateCsrfRequestAsync(
        AuthApiTestApp app,
        string path,
        object payload,
        string? trustedDeviceToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(
            backend.main.application.security.CsrfConfiguration.CsrfHeaderName,
            await app.GetCsrfTokenAsync());

        if (!string.IsNullOrWhiteSpace(trustedDeviceToken))
            request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, trustedDeviceToken);

        return request;
    }
}
