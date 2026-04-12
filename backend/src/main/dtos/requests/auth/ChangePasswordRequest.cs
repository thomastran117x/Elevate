using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.auth
{
    public class ChangePasswordRequest
    {
        [Required]
        [StringLength(30, MinimumLength = 4, ErrorMessage = "Password must be between 4 and 30 characters.")]
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
