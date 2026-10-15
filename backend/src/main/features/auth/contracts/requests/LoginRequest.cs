using System.ComponentModel.DataAnnotations;

namespace backend.main.features.auth.contracts.requests
{
    /// <summary>
    /// Credentials and client transport details for local sign-in.
    /// </summary>
    public class LoginRequest
    {
        [RequestText(RequestTextKind.Structured)]
        [Required]
        public required string Username
        {
            get; set;
        }

        [RequestText(RequestTextKind.Sensitive)]
        [Required]
        public required string Password
        {
            get; set;
        }

        public bool RememberMe { get; set; } = false;

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

        [RequestText(RequestTextKind.Sensitive)]
        public string? Captcha
        {
            get; set;
        }
    }
}
