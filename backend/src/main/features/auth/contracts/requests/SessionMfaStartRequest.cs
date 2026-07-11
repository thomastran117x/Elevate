using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class SessionMfaStartRequest
    {
        [Required]
        public required string Method
        {
            get; set;
        }
    }
}
