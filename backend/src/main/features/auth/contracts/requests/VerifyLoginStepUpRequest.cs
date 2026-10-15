using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class VerifyLoginStepUpRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Challenge
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Code
        {
            get; set;
        }
    }
}
