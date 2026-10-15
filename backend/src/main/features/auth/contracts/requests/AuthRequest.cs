using System.ComponentModel.DataAnnotations;

using backend.main.features.auth.password;

namespace backend.main.features.auth.contracts.requests
{
    public abstract class AuthRequest
    {
        [RequestText(RequestTextKind.Structured)]
        [Required]
        [EmailAddress]
        public required string Email
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [PasswordPolicy]
        public required string Password
        {
            get; set;
        }
        public bool RememberMe { get; set; } = false;

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Captcha
        {
            get; set;
        }
    }
}
