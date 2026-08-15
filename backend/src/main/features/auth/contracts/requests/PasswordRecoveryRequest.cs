using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class PasswordRecoveryRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 1)]
        public required string Username
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
