using System.ComponentModel.DataAnnotations;
using backend.main.configurations.security;

namespace backend.main.attributes.validation;

public class ValidRoleAttribute : ValidationAttribute
{
    public string[] AllowedRoles { get; set; } = AuthRoles.SignUpRoles;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string role)
            return ValidationResult.Success;

        if (
            AuthRoles.TryNormalize(role, out var normalizedRole)
            && AllowedRoles.Contains(normalizedRole, StringComparer.Ordinal)
        )
            return ValidationResult.Success;

        var formatted = string.Join(", ", AllowedRoles.Select(r => $"'{r}'"));
        return new ValidationResult($"Role must be one of: {formatted}.");
    }
}
