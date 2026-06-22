using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class MfaEnrollmentVerifyRequest
    {
        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        public required string Code
        {
            get; init;
        }

        [Required]
        public required string Challenge
        {
            get; init;
        }
    }
}
