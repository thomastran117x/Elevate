using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public class GoogleCodeRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Code
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string CodeVerifier
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Required]
        public required string RedirectUri
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        public string? Nonce
        {
            get; set;
        }
        [RequestText(RequestTextKind.Structured)]
        public string? Transport
        {
            get; set;
        }
        [RequestText(RequestTextKind.Structured)]
        public string? ReturnUrl
        {
            get; set;
        }
    }
}
