using System.Security.Claims;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.totp;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthTotpMfaControllerTests
{
    [Fact]
    public async Task StartEnrollment_ShouldReturnSecretPayload()
    {
        var totpService = new Mock<ITotpMfaEnrollmentService>();
        totpService.Setup(service => service.StartEnrollmentAsync(42, "member@example.com"))
            .ReturnsAsync(new TotpEnrollmentStartResponse
            {
                SecretKey = "BASE32SECRET",
                QrCodeUri = "otpauth://totp/EventXperience:member@example.com?secret=BASE32SECRET",
                ExpiresAtUtc = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc)
            });

        var controller = CreateController(totpService: totpService.Object);

        var result = await controller.StartEnrollment();

        var response = ExtractApiResponse<TotpEnrollmentStartResponse>(result, 200);
        response.Message.Should().Be("TOTP enrollment started. Scan the QR code with your authenticator app.");
        response.Data!.SecretKey.Should().Be("BASE32SECRET");
    }

    [Fact]
    public async Task VerifyEnrollment_ShouldReturnCombinedEnabledStatus()
    {
        var totpService = new Mock<ITotpMfaEnrollmentService>();
        totpService.Setup(service => service.VerifyEnrollmentAsync(42, "123456"))
            .ReturnsAsync(new TotpMfaEnrollment
            {
                UserId = 42,
                EncryptedSecret = "v1:encrypted",
                IsTotpMfaEnabled = true,
                EnrolledAtUtc = new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc)
            });

        var smsService = new Mock<IMfaEnrollmentService>();
        smsService.Setup(service => service.GetStatusAsync(42))
            .ReturnsAsync(new MfaStatusResponse
            {
                SmsEnrollmentAvailable = true,
                IsSmsMfaEnabled = true,
                MaskedPhoneNumber = "***-***-0123",
                PhoneVerifiedAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc)
            });

        var controller = CreateController(totpService.Object, smsService.Object);

        var result = await controller.VerifyEnrollment(new TotpEnrollmentVerifyRequest
        {
            Code = "123456"
        });

        var response = ExtractApiResponse<MfaStatusResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been enabled.");
        response.Data!.IsTotpMfaEnabled.Should().BeTrue();
        response.Data.IsSmsMfaEnabled.Should().BeTrue();
        response.Data.MaskedPhoneNumber.Should().Be("***-***-0123");
        response.Data.TotpEnrolledAtUtc.Should().Be(new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Disable_ShouldReturnCombinedDisabledStatus()
    {
        var totpService = new Mock<ITotpMfaEnrollmentService>();
        totpService.Setup(service => service.DisableAsync(42, "654321"))
            .ReturnsAsync(new TotpMfaEnrollment
            {
                UserId = 42,
                EncryptedSecret = "v1:encrypted",
                IsTotpMfaEnabled = false,
                EnrolledAtUtc = new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc),
                DisabledAtUtc = new DateTime(2026, 6, 27, 12, 15, 0, DateTimeKind.Utc)
            });

        var smsService = new Mock<IMfaEnrollmentService>();
        smsService.Setup(service => service.GetStatusAsync(42))
            .ReturnsAsync(new MfaStatusResponse
            {
                SmsEnrollmentAvailable = true,
                IsSmsMfaEnabled = false,
                MaskedPhoneNumber = "***-***-0123"
            });

        var controller = CreateController(totpService.Object, smsService.Object);

        var result = await controller.Disable(new TotpDisableRequest
        {
            Code = "654321"
        });

        var response = ExtractApiResponse<MfaStatusResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been disabled.");
        response.Data!.IsTotpMfaEnabled.Should().BeFalse();
        response.Data.IsSmsMfaEnabled.Should().BeFalse();
    }

    private static AuthTotpMfaController CreateController(
        ITotpMfaEnrollmentService? totpService = null,
        IMfaEnrollmentService? smsService = null)
    {
        totpService ??= new Mock<ITotpMfaEnrollmentService>().Object;
        smsService ??= new Mock<IMfaEnrollmentService>().Object;

        return new AuthTotpMfaController(totpService, smsService)
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
                    ], "TestAuth"))
                }
            }
        };
    }

    private static ApiResponse<T> ExtractApiResponse<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
