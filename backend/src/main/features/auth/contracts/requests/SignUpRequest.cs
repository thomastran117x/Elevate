using System.ComponentModel.DataAnnotations;
using backend.main.shared.attributes.validation;

namespace backend.main.features.auth.contracts.requests
{

    public class SignUpRequest : AuthRequest
    {
        [Required]
        [ValidRole]
        public required string Usertype
        {
            get; set;
        }
    }
}
