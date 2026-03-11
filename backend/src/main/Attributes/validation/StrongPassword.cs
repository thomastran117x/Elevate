using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace backend.app.attributes.validation;

public class StrongPasswordAttribute : ValidationAttribute
{
    public int MinimumLength { get; set; } = 8;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string password)
            return ValidationResult.Success;

        if (password.Length < MinimumLength)
            return new ValidationResult(
                $"Password must be at least {MinimumLength} characters long."
            );

        if (!Regex.IsMatch(password, @"[A-Z]"))
            return new ValidationResult("Password must contain at least one uppercase letter.");

        if (!Regex.IsMatch(password, @"[a-z]"))
            return new ValidationResult("Password must contain at least one lowercase letter.");

        if (!Regex.IsMatch(password, @"\d"))
            return new ValidationResult("Password must contain at least one number.");

        if (!Regex.IsMatch(password, @"[\W_]"))
            return new ValidationResult("Password must contain at least one special character.");

        return ValidationResult.Success;
    }
}
