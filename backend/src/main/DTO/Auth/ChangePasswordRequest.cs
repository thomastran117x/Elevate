using System.ComponentModel.DataAnnotations;

namespace backend.main.DTOs
{
    public class ChangePasswordRequest
    {
        [Required]
        [StringLength(30, MinimumLength = 4, ErrorMessage = "Password must be between 4 and 30 characters.")]
        public required string Password
        {
            get; set;
        }
    }
}
