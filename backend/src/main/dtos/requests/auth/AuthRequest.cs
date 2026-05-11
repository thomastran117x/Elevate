using System.ComponentModel.DataAnnotations;
using backend.main.shared.attributes.validation;

namespace backend.main.dtos.requests.auth
{
    public abstract class AuthRequest
    {
        [Required]
        [EmailAddress]
        public required string Email
        {
            get; set;
        }

        [Required]
        [StrongPassword]
        public required string Password
        {
            get; set;
        }
        public bool RememberMe { get; set; } = false;

        [Required]
        public required string Captcha
        {
            get; set;
        }
    }
}
