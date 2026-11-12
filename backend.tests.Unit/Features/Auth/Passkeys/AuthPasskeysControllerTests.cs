using System.Security.Claims;
using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.abuse;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.passkeys;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class AuthPasskeysControllerTests
{
    private static readonly PasskeyAccount Account = new(42, "member@example.com", "session-42", 5);
    private static readonly JsonElement Credential = JsonDocument.Parse("{\"id\":\"abc\"}").RootElement;
    private static readonly PasskeyCeremonyOptionsResponse Options = new()
    {
        CeremonyId = "ceremony-id",
        PublicKey = JsonDocument.Parse("{\"challenge\":\"abc\"}").RootElement,
        ExpiresAtUtc = new DateTime(2026, 11, 11, 12, 5, 0, DateTimeKind.Utc),
    };

    private readonly Mock<IPasskeyManagementService> _management = new();
    private readonly Mock<IPasskeyAuthenticationService> _authentication = new();
    private readonly Mock<IAuthAbuseProtectionService> _abuse = new();

    [Fact]
    public async Task List_ShouldReturnThePasskeys()
    {
        _management.Setup(m => m.ListAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeysResponse { Passkeys = [new PasskeyResponse { Id = 1, Name = "Laptop" }], Limit = 10 });

        var data = Extract<PasskeysResponse>(await CreateController().List(), 200).Data!;

        data.Passkeys.Single().Name.Should().Be("Laptop");
        typeof(PasskeyResponse).GetProperties().Select(p => p.Name)
            .Should().NotContain(["CredentialId", "PublicKey", "UserHandle"]);
    }

    [Fact]
    public async Task StartRegistration_ShouldPassTheAccessTokensSession()
    {
        _management.Setup(m => m.StartRegistrationAsync(Account, "Laptop", It.IsAny<CancellationToken>())).ReturnsAsync(Options);

        var data = Extract<PasskeyCeremonyOptionsResponse>(
            await CreateController().StartRegistration(new PasskeyRegistrationOptionsRequest { Name = "Laptop" }), 200).Data!;

        data.CeremonyId.Should().Be("ceremony-id");
    }

    [Fact]
    public async Task StartRegistration_ShouldRefuseATokenWithoutASession()
    {
        var result = await CreateController(withSession: false)
            .StartRegistration(new PasskeyRegistrationOptionsRequest());

        ((ObjectResult)result).StatusCode.Should().Be(401);
        _management.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteRegistration_ShouldReturnTheNewPasskey()
    {
        _management.Setup(m => m.CompleteRegistrationAsync(Account, "ceremony-id", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyResponse { Id = 3, Name = "Phone" });

        var data = Extract<PasskeyResponse>(
            await CreateController().CompleteRegistration(Completion()), 200).Data!;

        data.Id.Should().Be(3);
    }

    [Fact]
    public async Task Rename_ShouldReturnTheRenamedPasskey()
    {
        _management.Setup(m => m.RenameAsync(42, 3, "Phone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyResponse { Id = 3, Name = "Phone" });

        var data = Extract<PasskeyResponse>(
            await CreateController().Rename(3, new RenamePasskeyRequest { Name = "Phone" }), 200).Data!;

        data.Name.Should().Be("Phone");
    }

    [Fact]
    public async Task Remove_ShouldReportTheLastSignInMethodConflict()
    {
        _management.Setup(m => m.RemoveAsync(Account, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(PasskeyConflictException.LastSignInMethod());

        var result = await CreateController().Remove(3);

        var failure = Extract<object?>(result, 409);
        failure.Error!.Code.Should().Be("LAST_SIGN_IN_METHOD");
    }

    [Fact]
    public async Task Remove_ShouldConfirmTheRemoval()
    {
        Extract<object?>(await CreateController().Remove(3), 200).Message.Should().Be("Passkey removed.");
        _management.Verify(m => m.RemoveAsync(Account, 3, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task StartSignIn_ShouldCheckTheSourceBudget_AndReturnOptions()
    {
        _authentication.Setup(a => a.StartSignInAsync()).ReturnsAsync(Options);

        var data = Extract<PasskeyCeremonyOptionsResponse>(await CreateController(anonymous: true).StartSignIn(), 200).Data!;

        data.CeremonyId.Should().Be("ceremony-id");
        _abuse.Verify(a => a.EnsureSourceAllowedAsync(AuthAbuseFlow.General, "203.0.113.7", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CompleteSignIn_ShouldReturnApiTokensInTheBody_ForAnApiClient()
    {
        GivenSignIn(SessionTransport.ApiToken);

        var data = Extract<LoginAuthenticationResponse>(
            await CreateController(anonymous: true).CompleteSignIn(SignIn("api")), 200).Data!;

        data.Type.Should().Be(AuthFlowResponseTypes.Authenticated);
        data.Auth!.AccessToken.Should().Be("access");
        data.Auth.RefreshToken.Should().Be("refresh");
        data.Auth.SessionBindingToken.Should().Be("binding");
    }

    [Fact]
    public async Task CompleteSignIn_ShouldKeepBrowserRefreshTokensOutOfTheBody()
    {
        GivenSignIn(SessionTransport.BrowserCookie);
        var controller = CreateController(anonymous: true);

        var data = Extract<LoginAuthenticationResponse>(await controller.CompleteSignIn(SignIn(null)), 200).Data!;

        data.Auth!.RefreshToken.Should().BeNull();
        data.Auth.SessionBindingToken.Should().BeNull();
        controller.Response.Headers.SetCookie.ToString().Should().Contain("refreshToken=");
    }

    [Fact]
    public async Task CompleteSignIn_ShouldReturnTheGenericRefusal()
    {
        _authentication.Setup(a => a.CompleteSignInAsync(
                "ceremony-id", It.IsAny<JsonElement>(), It.IsAny<SessionTransport>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PasskeyAuthenticationFailedException());

        var failure = Extract<object?>(await CreateController(anonymous: true).CompleteSignIn(SignIn(null)), 401);

        failure.Error!.Code.Should().Be("AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task CompleteSignIn_ShouldHideAnUnexpectedFault()
    {
        _authentication.Setup(a => a.CompleteSignInAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<SessionTransport>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret detail"));

        var result = (ObjectResult)await CreateController(anonymous: true).CompleteSignIn(SignIn(null));

        result.StatusCode.Should().Be(500);
        JsonSerializer.Serialize(result.Value).Should().NotContain("secret detail");
    }

    [Fact]
    public async Task StepUp_ShouldCheckTheAccountBudget_AndPassTheSession()
    {
        _authentication.Setup(a => a.StartStepUpAsync(Account, It.IsAny<CancellationToken>())).ReturnsAsync(Options);
        var controller = CreateController();

        Extract<PasskeyCeremonyOptionsResponse>(await controller.StartStepUp(), 200);
        Extract<object?>(await controller.CompleteStepUp(Completion()), 200);

        _authentication.Verify(a => a.CompleteStepUpAsync(Account, "ceremony-id", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()));
        _abuse.Verify(a => a.EnsureTargetAllowedAsync(
            AuthAbuseFlow.Verification, AuthAbuseTargetKind.AccountId, "42", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task StepUp_ShouldStopAtAnExhaustedBudget()
    {
        _abuse.Setup(a => a.EnsureSourceAllowedAsync(It.IsAny<AuthAbuseFlow>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TooManyRequestException("slow down"));

        ((ObjectResult)await CreateController().StartStepUp()).StatusCode.Should().Be(429);
        _authentication.VerifyNoOtherCalls();
    }

    /// <summary>
    /// CSRF on these routes is the middleware's job, before authentication. The MVC filter runs
    /// after it, so it would refuse a signed-in browser whose token was issued anonymously.
    /// </summary>
    [Theory]
    [InlineData(nameof(AuthPasskeysController.StartSignIn))]
    [InlineData(nameof(AuthPasskeysController.CompleteSignIn))]
    public void SignIn_ShouldLeaveCsrfToTheMiddleware(string action)
    {
        typeof(AuthPasskeysController).GetMethod(action)!
            .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true)
            .Should().BeEmpty();
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Post;
        request.Path = "/api/auth/passkeys/sign-in";
        backend.main.application.security.CsrfConfiguration.RequiresCsrfValidation(request).Should().BeTrue();
    }

    private void GivenSignIn(SessionTransport transport) =>
        _authentication.Setup(a => a.CompleteSignInAsync(
                "ceremony-id", It.IsAny<JsonElement>(), transport, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserToken(
                new Token("access", DateTime.UtcNow.AddMinutes(15), "refresh", "binding", TimeSpan.FromDays(1), transport),
                new User { Id = 42, Email = "member@example.com", Usertype = "participant" }));

    private static PasskeyCeremonyCompletionRequest Completion() => new() { CeremonyId = "ceremony-id", Credential = Credential };

    private static PasskeySignInRequest SignIn(string? transport) => new()
    {
        CeremonyId = "ceremony-id",
        Credential = Credential,
        Transport = transport,
    };

    private AuthPasskeysController CreateController(bool withSession = true, bool anonymous = false)
    {
        var claims = new List<Claim>();
        if (!anonymous)
        {
            claims.Add(new(ClaimTypes.NameIdentifier, "42"));
            claims.Add(new(ClaimTypes.Name, "member@example.com"));
            claims.Add(new(ClaimTypes.Role, "Participant"));
            if (withSession)
            {
                claims.Add(new Claim(TokenService.SessionIdClaimType, "session-42"));
                claims.Add(new Claim(TokenService.AuthVersionClaimType, "5"));
            }
        }

        return new AuthPasskeysController(
            _management.Object,
            _authentication.Object,
            _abuse.Object,
            new ClientRequestInfo { IpAddress = "203.0.113.7" })
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, anonymous ? null : "TestAuth")),
                },
            },
        };
    }

    private static ApiResponse<T> Extract<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
