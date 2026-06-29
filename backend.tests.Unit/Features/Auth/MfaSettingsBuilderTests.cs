using backend.main.features.auth;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.totp;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

[Collection(EnvironmentVariableTestCollection.Name)]
public class MfaSettingsBuilderTests
{
    [Fact]
    public async Task BuildAsync_ShouldReturnUnconfiguredState_WhenNoEnrollmentsExist()
    {
        using var scope = new EnvironmentVariableScope();
        var smsRepository = new Mock<IMfaEnrollmentRepository>();
        smsRepository.Setup(repository => repository.GetByUserIdAsync(42))
            .ReturnsAsync((SmsMfaEnrollment?)null);

        var totpRepository = new Mock<ITotpMfaEnrollmentRepository>();
        totpRepository.Setup(repository => repository.GetByUserIdAsync(42))
            .ReturnsAsync((TotpMfaEnrollment?)null);

        var builder = new MfaSettingsBuilder(smsRepository.Object, totpRepository.Object);

        var response = await builder.BuildAsync(42, "member@example.com");

        response.Email.MaskedEmail.Should().Be("m***@example.com");
        response.Email.IsEnabled.Should().BeTrue();

        response.Sms.EnrollmentAvailable.Should().BeTrue();
        response.Sms.IsConfigured.Should().BeFalse();
        response.Sms.IsEnabled.Should().BeFalse();
        response.Sms.MaskedPhoneNumber.Should().BeNull();
        response.Sms.PhoneVerifiedAtUtc.Should().BeNull();
        response.Sms.CanEnroll.Should().BeTrue();
        response.Sms.CanEnable.Should().BeFalse();
        response.Sms.CanDisable.Should().BeFalse();
        response.Sms.CanRemove.Should().BeFalse();

        response.Totp.EnrollmentAvailable.Should().BeTrue();
        response.Totp.IsConfigured.Should().BeFalse();
        response.Totp.IsEnabled.Should().BeFalse();
        response.Totp.EnrolledAtUtc.Should().BeNull();
        response.Totp.DisabledAtUtc.Should().BeNull();
        response.Totp.CanEnroll.Should().BeTrue();
        response.Totp.CanEnable.Should().BeFalse();
        response.Totp.CanDisable.Should().BeFalse();
        response.Totp.CanRemove.Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_ShouldReturnConfiguredSmsAndDisabledTotpState_WithExpectedActions()
    {
        using var scope = new EnvironmentVariableScope();
        var verifiedAtUtc = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);
        var enrolledAtUtc = new DateTime(2026, 6, 27, 12, 5, 0, DateTimeKind.Utc);
        var disabledAtUtc = new DateTime(2026, 6, 27, 12, 15, 0, DateTimeKind.Utc);

        var smsRepository = new Mock<IMfaEnrollmentRepository>();
        smsRepository.Setup(repository => repository.GetByUserIdAsync(42))
            .ReturnsAsync(new SmsMfaEnrollment
            {
                UserId = 42,
                PhoneNumber = "+14165550123",
                IsSmsMfaEnabled = true,
                PhoneVerifiedAtUtc = verifiedAtUtc,
            });

        var totpRepository = new Mock<ITotpMfaEnrollmentRepository>();
        totpRepository.Setup(repository => repository.GetByUserIdAsync(42))
            .ReturnsAsync(new TotpMfaEnrollment
            {
                UserId = 42,
                EncryptedSecret = "v1:encrypted",
                IsTotpMfaEnabled = false,
                EnrolledAtUtc = enrolledAtUtc,
                DisabledAtUtc = disabledAtUtc,
            });

        var builder = new MfaSettingsBuilder(smsRepository.Object, totpRepository.Object);

        var response = await builder.BuildAsync(42, "jane@example.com");

        response.Email.MaskedEmail.Should().Be("j***@example.com");

        response.Sms.IsConfigured.Should().BeTrue();
        response.Sms.IsEnabled.Should().BeTrue();
        response.Sms.MaskedPhoneNumber.Should().Be("***-***-0123");
        response.Sms.PhoneVerifiedAtUtc.Should().Be(verifiedAtUtc);
        response.Sms.CanEnroll.Should().BeTrue();
        response.Sms.CanEnable.Should().BeFalse();
        response.Sms.CanDisable.Should().BeTrue();
        response.Sms.CanRemove.Should().BeTrue();

        response.Totp.EnrollmentAvailable.Should().BeTrue();
        response.Totp.IsConfigured.Should().BeTrue();
        response.Totp.IsEnabled.Should().BeFalse();
        response.Totp.EnrolledAtUtc.Should().Be(enrolledAtUtc);
        response.Totp.DisabledAtUtc.Should().Be(disabledAtUtc);
        response.Totp.CanEnroll.Should().BeFalse();
        response.Totp.CanEnable.Should().BeTrue();
        response.Totp.CanDisable.Should().BeFalse();
        response.Totp.CanRemove.Should().BeTrue();
    }

    [Theory]
    [InlineData("jane@example.com", "j***@example.com")]
    [InlineData("x@example.com", "*@example.com")]
    [InlineData("missing-at", "***")]
    public void MaskEmail_ShouldApplyExpectedRules(string email, string expected)
    {
        PhoneNumberFormatter.MaskEmail(email).Should().Be(expected);
    }

    [Theory]
    [InlineData("  (416) 555-0123  ", "+14165550123")]
    [InlineData("1-416-555-0123", "+14165550123")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    [InlineData("41655501234", "+41655501234")]
    public void Normalize_ShouldAcceptSupportedFormats(string rawPhoneNumber, string expected)
    {
        PhoneNumberFormatter.Normalize(rawPhoneNumber).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "Phone number is required.")]
    [InlineData("   ", "Phone number is required.")]
    [InlineData("+1-23", "Phone number must be a valid international number.")]
    [InlineData("12345", "Phone number must be a valid mobile number.")]
    public void Normalize_ShouldRejectInvalidFormats(string? rawPhoneNumber, string expectedMessage)
    {
        var act = () => PhoneNumberFormatter.Normalize(rawPhoneNumber!);

        act.Should().Throw<backend.main.shared.exceptions.http.BadRequestException>()
            .WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("+14165550123", "***-***-0123")]
    public void Mask_ShouldPreserveShortValues_AndHideLongPhoneNumbers(string phoneNumber, string expected)
    {
        PhoneNumberFormatter.Mask(phoneNumber).Should().Be(expected);
    }
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope()
        {
            Set("AUTH_TOTP_MFA_ENROLLMENT_ENABLED", "true");
        }

        private void Set(string key, string? value)
        {
            _originals[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}

