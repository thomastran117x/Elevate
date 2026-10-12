using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// The request-level part of the password policy: the length range, checked before the request
    /// reaches a service. Content rules need the account and run in <see cref="IPasswordPolicy"/>.
    /// There are deliberately no composition rules; spaces, Unicode and any symbol are allowed.
    /// </summary>
    public sealed class PasswordPolicyAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string password)
                return ValidationResult.Success;

            var options = validationContext.GetService(typeof(IOptions<PasswordPolicyOptions>))
                is IOptions<PasswordPolicyOptions> configured
                ? configured.Value
                : new PasswordPolicyOptions();

            var length = PasswordText.Length(password);
            if (length < options.MinLength || length > options.MaxLength)
                return new ValidationResult(PasswordPolicyMessages.Length(options.MinLength, options.MaxLength));

            return ValidationResult.Success;
        }
    }
}
