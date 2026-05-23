using System.ComponentModel.DataAnnotations;

using backend.app.shared.attributes.validation;
using backend.main.shared.attributes.validation;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

namespace backend.tests.Unit.Shared.Attributes.Validation;

public class ValidationAttributeTests
{
    [Fact]
    public void StrongPassword_ShouldRejectWeakPasswords_AndAcceptStrongOnes()
    {
        var attribute = new StrongPasswordAttribute();

        Validate(attribute, "short")!.ErrorMessage.Should().Be("Password must be at least 8 characters long.");
        Validate(attribute, "lowercase1!")!.ErrorMessage.Should().Be("Password must contain at least one uppercase letter.");
        Validate(attribute, "UPPERCASE1!")!.ErrorMessage.Should().Be("Password must contain at least one lowercase letter.");
        Validate(attribute, "NoNumber!")!.ErrorMessage.Should().Be("Password must contain at least one number.");
        Validate(attribute, "NoSpecial1")!.ErrorMessage.Should().Be("Password must contain at least one special character.");
        Validate(attribute, "StrongPass1!").Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ValidRole_ShouldNormalizeKnownRoles_AndRejectUnknownOnes()
    {
        var attribute = new ValidRoleAttribute();

        Validate(attribute, " organizer ").Should().Be(ValidationResult.Success);
        Validate(attribute, "guest")!.ErrorMessage.Should().Be("Role must be one of: 'Participant', 'Organizer', 'Volunteer'.");
    }

    [Fact]
    public void ValidCaptchaToken_ShouldRejectWhitespaceAndShortTokens()
    {
        var attribute = new ValidCaptchaTokenAttribute { MinimumLength = 10 };

        Validate(attribute, "   ")!.ErrorMessage.Should().Be("Captcha token is required.");
        Validate(attribute, "short")!.ErrorMessage.Should().Be("Captcha token must be at least 10 characters.");
        Validate(attribute, "abcdefghijk").Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void MaxFileSize_ShouldRejectFilesAboveLimit()
    {
        var attribute = new MaxFileSizeAttribute(1024 * 1024);

        Validate(attribute, CreateFormFile("image.png", 512 * 1024)).Should().Be(ValidationResult.Success);
        Validate(attribute, CreateFormFile("image.png", 2 * 1024 * 1024))!.ErrorMessage.Should().Be("File size must be less than 1MB");
    }

    [Fact]
    public void AllowedExtensions_ShouldAcceptKnownExtensions_CaseInsensitively()
    {
        var attribute = new AllowedExtensionsAttribute([".png", ".jpg"]);

        Validate(attribute, CreateFormFile("poster.PNG", 128)).Should().Be(ValidationResult.Success);
        Validate(attribute, CreateFormFile("archive.pdf", 128))!.ErrorMessage.Should().Be("Invalid file type. Allowed: .png, .jpg");
    }

    private static ValidationResult? Validate(ValidationAttribute attribute, object? value)
    {
        return attribute.GetValidationResult(value, new ValidationContext(new object()));
    }

    private static IFormFile CreateFormFile(string fileName, int length)
    {
        var bytes = new byte[length];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, length, "file", fileName);
    }
}
