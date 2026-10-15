using System.ComponentModel.DataAnnotations;

using backend.main.features.auth.password;

namespace backend.main.features.auth.contracts.requests
{
    public class ResetPasswordRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [PasswordPolicy]
        public required string Password
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        public string? Code
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        public string? Challenge
        {
            get; set;
        }
    }
}
