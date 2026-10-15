using System.ComponentModel.DataAnnotations;

namespace backend.main.features.profile.contracts.requests
{
    public class ChangeEmailRequest
    {
        [RequestText(RequestTextKind.Structured)]
        [Required]
        [EmailAddress]
        [MaxLength(254)]
        public required string NewEmail
        {
            get; set;
        }

        /// <summary>
        /// Required for accounts that have a password; omitted by OAuth-only accounts, which have
        /// none to prove. MFA step-up gates the endpoint either way.
        /// </summary>
        [RequestText(RequestTextKind.Sensitive)]
        public string? CurrentPassword
        {
            get; set;
        }
    }
}
