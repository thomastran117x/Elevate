using System.ComponentModel.DataAnnotations;
using backend.main.shared.attributes.validation;

namespace backend.main.dtos.requests.auth
{
    public class ChangePasswordRequest
    {
        [Required]
        [StrongPassword]
        public required string Password
        {
            get; set;
        }

        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        public string? Code
        {
            get; set;
        }

        public string? Challenge
        {
            get; set;
        }
    }
}
