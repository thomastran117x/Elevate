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

    [Fact]
    public void ImageContent_ShouldJudgeTheBytes_NotTheNameOrTheDeclaredType()
    {
        var attribute = new ImageContentAttribute();

        // A real PNG carrying a .jpg name and an image/jpeg header is still a PNG.
        Validate(attribute, CreateFormFile("avatar.jpg", PngBytes(), "image/jpeg"))
            .Should().Be(ValidationResult.Success);

        // Arbitrary bytes dressed up as a PNG are not.
        Validate(attribute, CreateFormFile("avatar.png", [0x01, 0x02, 0x03, 0x04], "image/png"))!
            .ErrorMessage.Should().Be("The file content must be a JPEG, PNG, WEBP, or GIF image.");

        // An SVG-prefixed polyglot is rejected on its bytes alone.
        Validate(attribute, CreateFormFile("avatar.png", "<svg onload=alert(1)>"u8.ToArray(), "image/png"))!
            .ErrorMessage.Should().Be("The file content must be a JPEG, PNG, WEBP, or GIF image.");

        Validate(attribute, "not-a-file").Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void ImageContent_ShouldLeaveTheFileReadableForTheSubsequentUpload()
    {
        // The attribute reads the header during model validation and the blob service reads the
        // whole file again afterwards. If the first read consumed or disposed the shared buffer,
        // the upload would silently store a truncated blob rather than throwing, and no
        // integration test could catch it: the fake blob service never opens the stream.
        var content = new byte[2048];
        PngBytes().CopyTo(content, 0);
        Random.Shared.NextBytes(content.AsSpan(PngBytes().Length));

        var file = CreateFormFile("avatar.png", content, "image/png");

        Validate(new ImageContentAttribute(), file).Should().Be(ValidationResult.Success);

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        buffer.ToArray().Should().Equal(content);
    }

    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static IFormFile CreateFormFile(string fileName, byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
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
