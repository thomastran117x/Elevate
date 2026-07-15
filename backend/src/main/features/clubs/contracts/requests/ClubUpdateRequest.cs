using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.contracts.requests
{
    /// <summary>
    /// JSON payload used to update an existing club.
    /// </summary>
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

        [Required(ErrorMessage = "Club image URL is required.")]
        [Url(ErrorMessage = "Club image URL must be a valid URL.")]
        public string ClubImageUrl { get; set; } = string.Empty;

        /// <summary>Optional hero/banner image URL. Send null/empty to clear the banner.</summary>
        [Url(ErrorMessage = "Banner image URL must be a valid URL.")]
        public string? BannerImageUrl
        {
            get; set;
        }
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
