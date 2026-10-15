using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class OtpVerificationRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        public required string Code
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Challenge
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        public string? Transport
        {
            get; set;
        }
    }
}
