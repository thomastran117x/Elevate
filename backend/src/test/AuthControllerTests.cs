using backend.main.dtos.general;
using backend.main.implementation.controllers;
using backend.main.services.interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
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
        authService.Verify(service => service.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void VerifyDevice_Get_RedirectsToFrontendWithoutConsumingToken()
    {
        var authService = new Mock<IAuthService>(MockBehavior.Strict);
        var controller = CreateController(authService);

        var result = controller.VerifyDevice("device-token");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("http://localhost:3090/auth/device/verify?token=device-token");
        authService.Verify(service => service.VerifyDeviceLoginAsync(It.IsAny<string>()), Times.Never);
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
        authService.Verify(service => service.VerifyAsync(It.IsAny<string>()), Times.Never);
    }

    private static AuthController CreateController(
        Mock<IAuthService> authService,
        Dictionary<string, string?>? configValues = null
    )
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        return new AuthController(
            authService.Object,
            Mock.Of<IAntiforgery>(),
            Mock.Of<ICaptchaService>(),
            new ClientRequestInfo(),
            config
        );
    }
}
