using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class EventUpdateRequest : IValidatableObject
    {
        [StringLength(30, MinimumLength = 3)]
        public string Name { get; set; } = null!;

        [StringLength(200, MinimumLength = 10)]
        public string Description { get; set; } = null!;

        [StringLength(50)]
        public string Location { get; set; } = null!;

        [MaxLength(5, ErrorMessage = "A maximum of 5 images are allowed.")]
        public List<string>? ImageUrls { get; set; }

        public bool IsPrivate { get; set; }

        [Range(1, 10_000, ErrorMessage = "Max participants must be between 1 and 10,000.")]
        public int MaxParticipants { get; set; } = 100;

        [Range(0, 50_000, ErrorMessage = "Register cost must be between $0 and $50,000.")]
        public int RegisterCost { get; set; } = 0;

        [Required]
        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

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

            if (ImageUrls != null)
            {
                foreach (var url in ImageUrls)
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        yield return new ValidationResult(
                            $"'{url}' is not a valid HTTPS URL.",
                            new[] { nameof(ImageUrls) });
                }
            }
        }
    }
}
