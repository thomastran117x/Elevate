using System.ComponentModel.DataAnnotations;

namespace backend.app.attributes.validation;

public class ValidRoleAttribute : ValidationAttribute
{
    private static readonly string[] DefaultAllowedRoles = ["student", "teacher", "assistant"];

    public string[] AllowedRoles { get; set; } = DefaultAllowedRoles;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string role)
            return ValidationResult.Success;

        if (AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Success;

        var formatted = string.Join(", ", AllowedRoles.Select(r => $"'{r}'"));
        return new ValidationResult($"Role must be one of: {formatted}.");
    }
}
