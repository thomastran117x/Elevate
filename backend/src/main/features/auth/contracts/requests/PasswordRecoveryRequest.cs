using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class PasswordRecoveryRequest
    {
        [Required]
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
