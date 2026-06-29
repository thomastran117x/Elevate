using System.Security.Claims;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthMfaControllerTests
{
    [Fact]
    public async Task GetStatus_ShouldReturnCurrentMfaSettings()
    {
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(smsConfigured: true, smsEnabled: true));

        var controller = CreateController(settingsBuilder: settingsBuilder.Object);

        var result = await controller.GetStatus();

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Data!.Email.MaskedEmail.Should().Be("m***@example.com");
        response.Data.Sms.IsEnabled.Should().BeTrue();
        response.Data.Totp.IsConfigured.Should().BeFalse();
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
    public async Task StartEnable_ShouldReturnChallengePayload()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.StartEnableAsync(42)).ReturnsAsync(new MfaChallengeResponse
        {
            Challenge = "challenge-2",
            ExpiresAtUtc = new DateTime(2026, 6, 22, 16, 30, 0, DateTimeKind.Utc),
            Channel = "sms",
            MaskedDestination = "***-***-0123",
        });

        var controller = CreateController(service.Object);

        var result = await controller.StartEnable();

        var response = ExtractApiResponse<MfaChallengeResponse>(result, 200);
        response.Data!.Challenge.Should().Be("challenge-2");
        response.Message.Should().Be("SMS MFA re-enable code sent.");
    }

    [Fact]
    public async Task VerifyEnrollment_ShouldReturnRefreshedSettings()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.VerifyEnrollmentAsync(42, "654321", "challenge-1")).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 42,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = true,
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 22, 15, 31, 0, DateTimeKind.Utc),
        });
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(smsConfigured: true, smsEnabled: true));

        var controller = CreateController(service.Object, settingsBuilder.Object);

        var result = await controller.VerifyEnrollment(new MfaEnrollmentVerifyRequest
        {
            Code = "654321",
            Challenge = "challenge-1",
        });

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("SMS MFA has been enabled.");
        response.Data!.Sms.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_ShouldReturnRefreshedSettings()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.DisableAsync(42)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 42,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = false,
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 22, 15, 31, 0, DateTimeKind.Utc),
        });
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(smsConfigured: true, smsEnabled: false));

        var controller = CreateController(service.Object, settingsBuilder.Object);

        var result = await controller.Disable(new MfaDisableRequest());

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("SMS MFA has been disabled.");
        response.Data!.Sms.IsEnabled.Should().BeFalse();
        response.Data.Sms.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_ShouldReturnRefreshedSettings()
    {
        var service = new Mock<IMfaEnrollmentService>();
        service.Setup(s => s.RemoveAsync(42)).Returns(Task.CompletedTask);
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(smsConfigured: false, smsEnabled: false));

        var controller = CreateController(service.Object, settingsBuilder.Object);

        var result = await controller.Remove(new MfaDisableRequest());

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("SMS MFA has been removed.");
        response.Data!.Sms.IsConfigured.Should().BeFalse();
    }

    private static AuthMfaController CreateController(
        IMfaEnrollmentService? service = null,
        IMfaSettingsBuilder? settingsBuilder = null)
    {
        service ??= new Mock<IMfaEnrollmentService>().Object;
        settingsBuilder ??= new Mock<IMfaSettingsBuilder>().Object;

        return new AuthMfaController(service, settingsBuilder)
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

    private static MfaSettingsResponse CreateSettings(bool smsConfigured, bool smsEnabled) => new()
    {
        Email = new EmailMfaSettingsDto
        {
            MaskedEmail = "m***@example.com",
            IsEnabled = true,
        },
        Sms = new SmsMfaSettingsDto
        {
            EnrollmentAvailable = true,
            IsConfigured = smsConfigured,
            IsEnabled = smsEnabled,
            MaskedPhoneNumber = smsConfigured ? "***-***-0123" : null,
            PhoneVerifiedAtUtc = smsConfigured ? new DateTime(2026, 6, 22, 15, 31, 0, DateTimeKind.Utc) : null,
            CanEnroll = true,
            CanEnable = smsConfigured && !smsEnabled,
            CanDisable = smsEnabled,
            CanRemove = smsConfigured,
        },
        Totp = new TotpMfaSettingsDto
        {
            EnrollmentAvailable = true,
            IsConfigured = false,
            IsEnabled = false,
            CanEnroll = true,
            CanEnable = false,
            CanDisable = false,
            CanRemove = false,
        },
    };

    private static ApiResponse<T> ExtractApiResponse<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
