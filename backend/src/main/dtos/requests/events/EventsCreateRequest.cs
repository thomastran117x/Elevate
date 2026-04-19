using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

using backend.main.models.enums;

namespace backend.main.dtos.requests.events
{
    public class EventCreateRequest : IValidatableObject
    {
        [Required]
        [StringLength(30, MinimumLength = 3)]
        public string Name { get; set; } = null!;

        [Required]
        [StringLength(200, MinimumLength = 10)]
        public string Description { get; set; } = null!;

        [Required]
        [StringLength(50)]
        public string Location { get; set; } = null!;

        [Required]
        [MinLength(1, ErrorMessage = "At least one image is required.")]
        [MaxLength(5, ErrorMessage = "A maximum of 5 images are allowed.")]
        public List<string> ImageUrls { get; set; } = new();

        public bool IsPrivate { get; set; }

        [Range(1, 10_000, ErrorMessage = "Max participants must be between 1 and 10,000.")]
        public int MaxParticipants { get; set; } = 100;

        [Range(0, 50_000, ErrorMessage = "Register cost must be between $0 and $50,000.")]
        public int RegisterCost { get; set; } = 0;

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        [Required(ErrorMessage = "Category is required.")]
        [EnumDataType(typeof(EventCategory))]
        public EventCategory Category { get; set; }

        [StringLength(100)]
        public string? VenueName { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [Range(-90, 90)]
        public double? Latitude { get; set; }

        [Range(-180, 180)]
        public double? Longitude { get; set; }

        [MaxLength(10, ErrorMessage = "A maximum of 10 tags are allowed.")]
        public List<string>? Tags { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartTime < DateTime.UtcNow)
            {
                yield return new ValidationResult(
                    "StartTime must be in the future.",
                    new[] { nameof(StartTime) });
            }

            if (EndTime.HasValue && EndTime <= StartTime)
            {
                yield return new ValidationResult(
                    "EndTime must be later than StartTime.",
                    new[] { nameof(EndTime) });
            }

            if (IsPrivate && RegisterCost > 0)
            {
                yield return new ValidationResult(
                    "Private events cannot require a registration fee.",
                    new[] { nameof(RegisterCost), nameof(IsPrivate) });
            }

            foreach (var url in ImageUrls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    yield return new ValidationResult(
                        $"'{url}' is not a valid HTTPS URL.",
                        new[] { nameof(ImageUrls) });
            }

            if (Latitude.HasValue != Longitude.HasValue)
            {
                yield return new ValidationResult(
                    "Latitude and Longitude must both be provided, or both omitted.",
                    new[] { nameof(Latitude), nameof(Longitude) });
            }

            if (Tags != null)
            {
                foreach (var tag in Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag) || tag.Length > 30
                        || !Regex.IsMatch(tag, "^[a-zA-Z0-9-]+$"))
                    {
                        yield return new ValidationResult(
                            $"Tag '{tag}' is invalid. Tags must be 1-30 chars, alphanumeric or dashes.",
                            new[] { nameof(Tags) });
                    }
                }
            }
        }
    }
}
