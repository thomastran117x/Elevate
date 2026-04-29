using System.ComponentModel.DataAnnotations;
using backend.main.attributes.validation;

namespace backend.main.dtos.requests.auth
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
