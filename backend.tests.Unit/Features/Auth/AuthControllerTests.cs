using backend.main.features.auth;
using backend.main.features.auth.captcha;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.shared.responses;

using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthControllerTests
{
    [Fact]
    public void LocalVerify_ShouldRedirectToFrontendVerificationPage()
    {
        var controller = CreateController();

        var result = controller.LocalVerify("token with spaces");

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://frontend.example.com/auth/verify?token=token%20with%20spaces");
    }

    [Fact]
    public async Task LocalAuthenticate_ShouldWriteBrowserRefreshCookies()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(service => service.LoginAsync(
                "browser@example.com",
                "Password123!",
                SessionTransport.BrowserCookie,
                false))
            .ReturnsAsync(CreateUserToken(
                "browser@example.com",
                SessionTransport.BrowserCookie,
                refreshToken: "browser-refresh",
                bindingToken: "browser-binding"));

        var controller = CreateController(authService: authService);

        var result = await controller.LocalAuthenticate(new LoginRequest
        {
            Email = "browser@example.com",
            Password = "Password123!",
            Captcha = "captcha"
        });

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<AuthenticatedSessionResponse>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.RefreshToken.Should().BeNull();
        response.Data.SessionBindingToken.Should().BeNull();
        controller.Response.Headers.SetCookie.Should().Contain(value => value.Contains("refreshToken="));
        controller.Response.Headers.SetCookie.Should().Contain(value => value.Contains("refreshBinding="));
    }

    [Fact]
    public async Task LocalAuthenticate_ShouldReturnApiRefreshTokensForApiTransport()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(service => service.LoginAsync(
                "api@example.com",
                "Password123!",
                SessionTransport.ApiToken,
                false))
            .ReturnsAsync(CreateUserToken(
                "api@example.com",
                SessionTransport.ApiToken,
                refreshToken: "api-refresh",
                bindingToken: "api-binding"));

        var controller = CreateController(authService: authService);

        var result = await controller.LocalAuthenticate(new LoginRequest
        {
            Email = "api@example.com",
            Password = "Password123!",
            Captcha = "captcha",
            Transport = SessionTransportResolver.ApiValue
        });

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<AuthenticatedSessionResponse>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.RefreshToken.Should().Be("api-refresh");
        response.Data.SessionBindingToken.Should().Be("api-binding");
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    private static AuthController CreateController(
        Mock<IAuthService>? authService = null,
        Mock<ICaptchaService>? captchaService = null)
    {
        authService ??= new Mock<IAuthService>();
        captchaService ??= new Mock<ICaptchaService>();
        captchaService.Setup(service => service.VerifyCaptchaAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://frontend.example.com"
            })
            .Build();

        var controller = new AuthController(
            authService.Object,
            Mock.Of<IAntiforgery>(),
            captchaService.Object,
            TestRequestInfoFactory.Browser(),
            configuration);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static UserToken CreateUserToken(
        string email,
        SessionTransport transport,
        string refreshToken,
        string bindingToken)
    {
        return new UserToken(
            new Token(
                "access-token",
                DateTime.UtcNow.AddMinutes(15),
                refreshToken,
                bindingToken,
                TimeSpan.FromDays(1),
                transport),
            new TestUserBuilder().WithEmail(email).Build());
    }
}
