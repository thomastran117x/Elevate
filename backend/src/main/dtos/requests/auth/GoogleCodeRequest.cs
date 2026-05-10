using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.auth
{
    public class GoogleCodeRequest
    {
        [Required]
        public required string Code { get; set; }

        [Required]
        public required string CodeVerifier { get; set; }

        [Required]
        public required string RedirectUri { get; set; }

        public string? Nonce { get; set; }
        public string? Transport { get; set; }
    }
}
