using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.auth
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
