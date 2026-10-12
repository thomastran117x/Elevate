using System.ComponentModel.DataAnnotations;

using backend.main.features.auth.password;

namespace backend.main.features.profile.contracts.requests
{
    public class ChangePasswordAuthenticatedRequest
    {
        [Required]
        public required string CurrentPassword
        {
            get; set;
        }

        [Required]
        [PasswordPolicy]
        public required string NewPassword
        {
            get; set;
        }
    }
}
