using System.ComponentModel.DataAnnotations;

namespace backend.main.features.clubs.contracts.requests
{
    /// <summary>
    /// JSON payload used to create a club.
    /// </summary>
    public class ClubCreateRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [Required]
        [StringLength(30, ErrorMessage = "Name cannot exceed 30 characters.")]
        public required string Name
        {
            get; set;
        }
        [RequestText(RequestTextKind.HumanMultiline)]
        [Required]
        [StringLength(30, ErrorMessage = "Description cannot exceed 30 characters.")]
        public required string Description
        {
            get; set;
        }
        [RequestText(RequestTextKind.Structured)]
        [Required]
        [RegularExpression("^(sport|sports|academic|social|cultural|game|gaming|music|other)$",
        ErrorMessage = "Clubtype must be one of: sport, sports, academic, social, cultural, game, gaming, music, other.")]
        public required string Clubtype
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Required(ErrorMessage = "Club image URL is required.")]
        [Url(ErrorMessage = "Club image URL must be a valid URL.")]
        public string ClubImageUrl { get; set; } = string.Empty;

        /// <summary>Optional hero/banner image URL. When omitted the public page shows a gradient placeholder.</summary>
        [RequestText(RequestTextKind.Structured)]
        [Url(ErrorMessage = "Banner image URL must be a valid URL.")]
        public string? BannerImageUrl
        {
            get; set;
        }

        /// <summary>Up to 5 display photos shown in a gallery on the public page (enforced server-side).</summary>
        [RequestText(RequestTextKind.Structured)]
        public List<string>? GalleryImageUrls
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Phone]
        public string? Phone
        {
            get; set;
        }
        [RequestText(RequestTextKind.Structured)]
        [EmailAddress]
        public string? Email
        {
            get; set;
        }

        [RequestText(RequestTextKind.Structured)]
        [Url(ErrorMessage = "Website URL must be a valid URL.")]
        public string? WebsiteUrl
        {
            get; set;
        }

        [RequestText(RequestTextKind.HumanSingleLine)]
        [StringLength(100, ErrorMessage = "Location cannot exceed 100 characters.")]
        public string? Location
        {
            get; set;
        }

        /// <summary>Maximum member capacity. 0 means unlimited. Defaults to 1000 when omitted.</summary>
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
