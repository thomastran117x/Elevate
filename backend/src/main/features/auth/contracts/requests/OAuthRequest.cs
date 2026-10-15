using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    public abstract class OAuthRequest
    {
        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Token
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
