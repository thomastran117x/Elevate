using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class SessionMfaVerifyRequest
    {
        [Required]
        public required string Method
        {
            get; set;
        }

        [Required]
        public required string Code
        {
            get; set;
        }
    }
}
