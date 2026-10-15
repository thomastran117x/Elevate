using System.ComponentModel.DataAnnotations;

using backend.main.shared.attributes.validation;

namespace backend.main.features.auth.contracts.requests
{

    public class SignUpRequest : AuthRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        public required string Username
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Required]
        [ValidRole]
        public required string Usertype
        {
            get; set;
        }
    }
}
