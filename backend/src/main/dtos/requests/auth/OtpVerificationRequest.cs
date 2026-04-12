using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.auth
{
    public sealed class OtpVerificationRequest
    {
        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        public required string Code { get; set; }

        [Required]
        public required string Challenge { get; set; }
    }
}
