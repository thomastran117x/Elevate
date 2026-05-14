using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.contracts.requests
{
    public class ClubUpdateRequest
    {
        [StringLength(30, ErrorMessage = "Name cannot exceed 30 characters.")]
        public required string Name
        {
            get; set;
        }
        [StringLength(30, ErrorMessage = "Name cannot exceed 30 characters.")]
        public required string Description
        {
            get; set;
        }
        [RegularExpression("^(sport|sports|academic|social|cultural|game|gaming|music|other)$",
        ErrorMessage = "Clubtype must be one of: sport, sports, academic, social, cultural, game, gaming, music, other.")]
        public required string Clubtype
        {
            get; set;
        }

        [Required(ErrorMessage = "Club image is required.")]
        public IFormFile ClubImage { get; set; } = default!;
        [Phone]
        public string? Phone
        {
            get; set;
        }
        [EmailAddress]
        public string? Email
        {
            get; set;
        }
    }
}
