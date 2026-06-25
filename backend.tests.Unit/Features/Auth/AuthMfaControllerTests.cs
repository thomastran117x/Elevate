using System.Security.Claims;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthMfaControllerTests
{
    [Fact]
    public async Task GetStatus_ShouldReturnCurrentMfaState()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.GetStatusAsync(42)).ReturnsAsync(new MfaStatusResponse
        {
            EnrollmentAvailable = true,
            IsSmsMfaEnabled = true,
            MaskedPhoneNumber = "***-***-0123",
        });

        var controller = CreateController(service.Object);

        var result = await controller.GetStatus();

        var response = ExtractApiResponse<MfaStatusResponse>(result, 200);
        response.Data!.IsSmsMfaEnabled.Should().BeTrue();
        response.Data.MaskedPhoneNumber.Should().Be("***-***-0123");
    }

    [Fact]
    public async Task StartEnrollment_ShouldReturnChallengePayload()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.StartEnrollmentAsync(42, "+14165550123")).ReturnsAsync(new MfaChallengeResponse
        {
            Challenge = "challenge-1",
            ExpiresAtUtc = new DateTime(2026, 6, 22, 15, 30, 0, DateTimeKind.Utc),
            Channel = "sms",
            MaskedDestination = "***-***-0123",
        });

        var controller = CreateController(service.Object);

        var result = await controller.StartEnrollment(new MfaEnrollmentStartRequest
        {
            PhoneNumber = "+14165550123",
        });

        var response = ExtractApiResponse<MfaChallengeResponse>(result, 200);
        response.Data!.Challenge.Should().Be("challenge-1");
        response.Message.Should().Be("SMS MFA enrollment code sent.");
    }

    [Fact]
    public async Task VerifyEnrollment_ShouldReturnEnabledStatus()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.VerifyEnrollmentAsync(42, "654321", "challenge-1")).ReturnsAsync(new MfaStatusResponse
        {
            EnrollmentAvailable = true,
            IsSmsMfaEnabled = true,
            MaskedPhoneNumber = "***-***-0123",
        });

        var controller = CreateController(service.Object);

        var result = await controller.VerifyEnrollment(new MfaEnrollmentVerifyRequest
        {
            Code = "654321",
            Challenge = "challenge-1",
        });

        var response = ExtractApiResponse<MfaStatusResponse>(result, 200);
        response.Message.Should().Be("SMS MFA has been enabled.");
        response.Data!.IsSmsMfaEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_ShouldReturnDisabledStatus()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.DisableAsync(42)).ReturnsAsync(new MfaStatusResponse
        {
            EnrollmentAvailable = true,
            IsSmsMfaEnabled = false,
            MaskedPhoneNumber = "***-***-0123",
        });

        var controller = CreateController(service.Object);

        var result = await controller.Disable(new MfaDisableRequest());

        var response = ExtractApiResponse<MfaStatusResponse>(result, 200);
        response.Message.Should().Be("SMS MFA has been disabled.");
        response.Data!.IsSmsMfaEnabled.Should().BeFalse();
    }

    private static AuthMfaController CreateController(IMfaEnrollmentService service)
    {
        return new AuthMfaController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(ClaimTypes.Name, "member@example.com"),
                        new Claim(ClaimTypes.Role, "Participant"),
                    ], "TestAuth")),
                },
            },
        };
    }

    private static ApiResponse<T> ExtractApiResponse<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
