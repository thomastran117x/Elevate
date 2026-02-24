using System.ComponentModel.DataAnnotations;

namespace backend.main.DTOs
{
    public class ForgotPasswordRequest
    {
        [Required]
        [EmailAddress]
        public required string Email
        {
            get; set;
        }
    }
}
