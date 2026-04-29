using backend.main.dtos.general;
using backend.main.dtos.requests.auth;
using backend.main.dtos.responses.auth;
using backend.main.implementation.controllers;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace backend.test;

public class AuthControllerTests
{
    [Fact]
    public void LocalVerify_Get_RedirectsToFrontendWithoutConsumingToken()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var controller = CreateController(authService);

        var result = controller.LocalVerify("email-token");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("http://localhost:3090/auth/verify?token=email-token");
        authService.Verify(
            service => service.VerifyAsync(It.IsAny<string>(), It.IsAny<SessionTransport>()),
            Times.Never
        );
    }

    [Fact]
    public void VerifyDevice_Get_RedirectsToFrontendWithoutConsumingToken()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var controller = CreateController(authService);

        var result = controller.VerifyDevice("device-token");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("http://localhost:3090/auth/device/verify?token=device-token");
        authService.Verify(
            service => service.VerifyDeviceLoginAsync(
                It.IsAny<string>(),
                It.IsAny<SessionTransport>()
            ),
            Times.Never
        );
    }

    [Fact]
    public void LocalVerify_Get_UsesConfiguredFrontendUrl()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var controller = CreateController(
            authService,
            new Dictionary<string, string?> { ["FRONTEND_URL"] = "https://app.eventxperience.test" }
        );

        var result = controller.LocalVerify("email-token");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("https://app.eventxperience.test/auth/verify?token=email-token");
        authService.Verify(
            service => service.VerifyAsync(It.IsAny<string>(), It.IsAny<SessionTransport>()),
            Times.Never
        );
    }

    [Fact]
    public async Task Refresh_ReturnsAccessTokenAliasForFrontendCompatibility()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(service => service.HandleTokensAsync(
                "refresh-token",
                "binding-token",
                SessionTransport.BrowserCookie
            ))
            .ReturnsAsync(new UserToken(
                new Token(
                    "access-token",
                    "refresh-token-2",
                    "binding-token-2",
                    TimeSpan.FromDays(1),
                    SessionTransport.BrowserCookie
                ),
                new User
                {
                    Id = 9,
                    Email = "organizer@example.com",
                    Usertype = "Organizer"
                }
            ));

        var controller = CreateController(authService);
        controller.HttpContext.Request.Headers.Cookie = $"{HttpUtility.RefreshCookieName}=refresh-token; {HttpUtility.RefreshBindingCookieName}=binding-token";

        var result = await controller.Refresh(new RefreshTokenRequest());

        var payload = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<AuthResponse>()
            .Subject;

        payload.Token.Should().Be("access-token");
        payload.AccessToken.Should().Be("access-token");
        payload.RefreshToken.Should().BeNull();
        payload.SessionBindingToken.Should().BeNull();
        controller.HttpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain(HttpUtility.RefreshCookieName);
        controller.HttpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain(HttpUtility.RefreshBindingCookieName);
    }

    [Fact]
    public async Task ApiRefresh_ReturnsRefreshSecretsForApiTransport()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(service => service.HandleTokensAsync(
                "refresh-token",
                "binding-token",
                SessionTransport.ApiToken
            ))
            .ReturnsAsync(new UserToken(
                new Token(
                    "access-token",
                    "refresh-token-2",
                    "binding-token-2",
                    TimeSpan.FromDays(1),
                    SessionTransport.ApiToken
                ),
                new User
                {
                    Id = 9,
                    Email = "organizer@example.com",
                    Usertype = "Organizer"
                }
            ));

        var controller = CreateController(authService);

        var result = await controller.ApiRefresh(new RefreshTokenRequest
        {
            RefreshToken = "refresh-token",
            SessionBindingToken = "binding-token"
        });

        var payload = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<AuthResponse>()
            .Subject;

        payload.Token.Should().Be("access-token");
        payload.RefreshToken.Should().Be("refresh-token-2");
        payload.SessionBindingToken.Should().Be("binding-token-2");
    }

    private static AuthController CreateController(
        Mock<IAuthService> authService,
        Dictionary<string, string?>? configValues = null
    )
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        var controller = new AuthController(
            authService.Object,
            Mock.Of<IAntiforgery>(),
            Mock.Of<ICaptchaService>(),
            new ClientRequestInfo(),
            config
        );

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }
}
