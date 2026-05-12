using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public required string Email
        {
            get; set;
        }

        [Required]
        public required string Password
        {
            get; set;
        }

        public bool RememberMe { get; set; } = false;

        public string? Transport { get; set; }

        [Required]
        public required string Captcha
        {
            get; set;
        }
    }
}
