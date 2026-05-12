using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public abstract class OAuthRequest
    {
        [Required]
        public required string Token
        {
            get; set;
        }

        public string? Transport { get; set; }
    }
}
