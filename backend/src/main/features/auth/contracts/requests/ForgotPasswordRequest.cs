using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class ForgotPasswordRequest
    {
        [Required]
        [EmailAddress]
        public required string Email
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
