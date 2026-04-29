using System.ComponentModel.DataAnnotations;
using backend.main.attributes.validation;

namespace backend.main.dtos.requests.auth
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
