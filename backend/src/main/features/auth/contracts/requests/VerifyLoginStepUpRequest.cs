using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class VerifyLoginStepUpRequest
    {
        [Required]
        public required string Challenge
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
