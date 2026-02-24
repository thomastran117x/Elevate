using System.ComponentModel.DataAnnotations;

namespace backend.main.DTOs
{

    public class SignUpRequest : AuthRequest
    {
        [Required]
        [RegularExpression("^(participant|organizer|volunteer)$",
        ErrorMessage = "Usertype must be 'participant', 'organizer' or 'volunteer'.")]
        public required string Usertype
        {
            get; set;
        }
    }
}
