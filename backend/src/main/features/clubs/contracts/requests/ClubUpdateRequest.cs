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
        [StringLength(30, ErrorMessage = "Description cannot exceed 30 characters.")]
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

        [Url(ErrorMessage = "Website URL must be a valid URL.")]
        public string? WebsiteUrl
        {
            get; set;
        }

        [StringLength(100, ErrorMessage = "Location cannot exceed 100 characters.")]
        public string? Location
        {
            get; set;
        }

        /// <summary>Maximum member capacity. 0 means unlimited.</summary>
        [Range(0, 100000, ErrorMessage = "Max members must be between 0 and 100000.")]
        public int? MaxMemberCount
        {
            get; set;
        }

        /// <summary>When true the club is invite-only and cannot be joined directly.</summary>
        public bool IsPrivate
        {
            get; set;
        }
    }
}
