using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class StartLoginStepUpRequest
    {
        [Required]
        public required string Challenge
        {
            get; set;
        }

        [Required]
        public required string Method
        {
            get; set;
        }
    }
}
