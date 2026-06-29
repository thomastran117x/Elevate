using System.Security.Claims;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.totp;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
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
    public async Task VerifyEnrollment_ShouldReturnRefreshedSettings()
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
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(isConfigured: true, isEnabled: true, disabledAtUtc: null));

        var controller = CreateController(totpService.Object, settingsBuilder.Object);

        var result = await controller.VerifyEnrollment(new TotpEnrollmentVerifyRequest
        {
            Code = "123456"
        });

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been enabled.");
        response.Data!.Totp.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_ShouldReturnRefreshedSettings()
    {
        var totpService = new Mock<ITotpMfaEnrollmentService>();
        totpService.Setup(service => service.EnableAsync(42, "654321"))
            .ReturnsAsync(new TotpMfaEnrollment
            {
                UserId = 42,
                EncryptedSecret = "v1:encrypted",
                IsTotpMfaEnabled = true,
                EnrolledAtUtc = new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc)
            });
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(isConfigured: true, isEnabled: true, disabledAtUtc: null));

        var controller = CreateController(totpService.Object, settingsBuilder.Object);

        var result = await controller.Enable(new TotpDisableRequest { Code = "654321" });

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been enabled.");
        response.Data!.Totp.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_ShouldReturnRefreshedSettings()
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
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(isConfigured: true, isEnabled: false, disabledAtUtc: new DateTime(2026, 6, 27, 12, 15, 0, DateTimeKind.Utc)));

        var controller = CreateController(totpService.Object, settingsBuilder.Object);

        var result = await controller.Disable(new TotpDisableRequest { Code = "654321" });

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been disabled.");
        response.Data!.Totp.IsEnabled.Should().BeFalse();
        response.Data.Totp.DisabledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_ShouldReturnRefreshedSettings()
    {
        var totpService = new Mock<ITotpMfaEnrollmentService>();
        totpService.Setup(service => service.RemoveAsync(42, "654321"))
            .Returns(Task.CompletedTask);
        var settingsBuilder = new Mock<IMfaSettingsBuilder>();
        settingsBuilder.Setup(builder => builder.BuildAsync(42, "member@example.com"))
            .ReturnsAsync(CreateSettings(isConfigured: false, isEnabled: false, disabledAtUtc: null));

        var controller = CreateController(totpService.Object, settingsBuilder.Object);

        var result = await controller.Remove(new TotpDisableRequest { Code = "654321" });

        var response = ExtractApiResponse<MfaSettingsResponse>(result, 200);
        response.Message.Should().Be("TOTP MFA has been removed.");
        response.Data!.Totp.IsConfigured.Should().BeFalse();
    }

    private static AuthTotpMfaController CreateController(
        ITotpMfaEnrollmentService? totpService = null,
        IMfaSettingsBuilder? settingsBuilder = null)
    {
        totpService ??= new Mock<ITotpMfaEnrollmentService>().Object;
        settingsBuilder ??= new Mock<IMfaSettingsBuilder>().Object;

        return new AuthTotpMfaController(totpService, settingsBuilder)
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

    private static MfaSettingsResponse CreateSettings(bool isConfigured, bool isEnabled, DateTime? disabledAtUtc) => new()
    {
        Email = new EmailMfaSettingsDto
        {
            MaskedEmail = "m***@example.com",
            IsEnabled = true,
        },
        Sms = new SmsMfaSettingsDto
        {
            EnrollmentAvailable = true,
            IsConfigured = true,
            IsEnabled = true,
            MaskedPhoneNumber = "***-***-0123",
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
            CanEnroll = true,
            CanEnable = false,
            CanDisable = true,
            CanRemove = true,
        },
        Totp = new TotpMfaSettingsDto
        {
            EnrollmentAvailable = true,
            IsConfigured = isConfigured,
            IsEnabled = isEnabled,
            EnrolledAtUtc = isConfigured ? new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc) : null,
            DisabledAtUtc = disabledAtUtc,
            CanEnroll = !isConfigured,
            CanEnable = isConfigured && !isEnabled,
            CanDisable = isEnabled,
            CanRemove = isConfigured,
        },
    };

    private static ApiResponse<T> ExtractApiResponse<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
