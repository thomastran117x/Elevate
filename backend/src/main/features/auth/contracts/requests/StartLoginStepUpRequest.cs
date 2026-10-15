using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class StartLoginStepUpRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Challenge
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Required]
        public required string Method
        {
            get; set;
        }
    }
}
