using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class MfaEnrollmentStartRequest
    {
        [Required]
        public required string PhoneNumber
        {
            get; init;
        }
    }
}
