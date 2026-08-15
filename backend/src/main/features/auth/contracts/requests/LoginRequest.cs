using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    /// <summary>
    /// Credentials and client transport details for local sign-in.
    /// </summary>
    public class LoginRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 1)]
        public required string Username
        {
            get; set;
        }

        [Required]
        public required string Password
        {
            get; set;
        }

        public bool RememberMe { get; set; } = false;

        public string? Transport
        {
            get; set;
        }

        public string? ReturnUrl
        {
            get; set;
        }

        [Required]
        public required string Captcha
        {
            get; set;
        }
    }
}
