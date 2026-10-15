using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class UsernameRecoveryRequest
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
        public required string Captcha
        {
            get; set;
        }
    }
}
