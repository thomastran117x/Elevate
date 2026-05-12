using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
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
