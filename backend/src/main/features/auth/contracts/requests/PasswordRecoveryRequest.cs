using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class PasswordRecoveryRequest
    {
        [RequestText(RequestTextKind.Structured)]
        [Required]
        public required string Username
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Captcha
        {
            get; set;
        }
    }
}
