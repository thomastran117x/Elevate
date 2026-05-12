using System.ComponentModel.DataAnnotations;
using backend.main.shared.attributes.validation;

namespace backend.main.features.auth.contracts.requests
{
    public sealed class CompleteOAuthSignupRequest
    {
        [Required]
        public required string SignupToken { get; set; }

        [Required]
        [ValidRole]
        public required string Usertype { get; set; }

        public string? Transport { get; set; }
    }
}
