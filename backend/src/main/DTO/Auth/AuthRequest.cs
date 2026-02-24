using System.ComponentModel.DataAnnotations;

namespace backend.main.DTOs
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
        [StringLength(30, MinimumLength = 4, ErrorMessage = "Password must be between 4 and 30 characters.")]
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
