using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class SessionMfaVerifyRequest
    {
        [RequestText(RequestTextKind.Structured)]
        [Required]
        public required string Method
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
