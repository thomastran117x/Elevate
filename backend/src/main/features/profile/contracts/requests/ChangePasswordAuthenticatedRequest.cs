using System.ComponentModel.DataAnnotations;

using backend.main.shared.attributes.validation;

namespace backend.main.features.profile.contracts.requests
{
    public class ChangePasswordAuthenticatedRequest
    {
        [Required]
        public required string CurrentPassword { get; set; }

        [Required]
        [StrongPassword]
        public required string NewPassword { get; set; }
    }
}
