using System.ComponentModel.DataAnnotations;

namespace backend.app.attributes.validation;

public class ValidCaptchaTokenAttribute : ValidationAttribute
{
    public int MinimumLength { get; set; } = 100;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string token)
            return ValidationResult.Success;

        if (string.IsNullOrWhiteSpace(token))
            return new ValidationResult("Captcha token is required.");

        if (token.Length < MinimumLength)
            return new ValidationResult(
                $"Captcha token must be at least {MinimumLength} characters."
            );

        return ValidationResult.Success;
    }
}
