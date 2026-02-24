using System.ComponentModel.DataAnnotations;

using backend.main.Attribute;

namespace backend.main.DTOs
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
        [AllowedExtensions(new[] { ".jpg", ".jpeg", ".png", ".webp" })]
        [MaxFileSize(5 * 1024 * 1024)]
        public IFormFile EventImage { get; set; } = null!;

        public bool IsPrivate
        {
            get; set;
        }

        [Range(1, 10_000, ErrorMessage = "Max participants must be between 1 and 10,000.")]
        public int MaxParticipants { get; set; } = 100;

        [Range(0, 50_000, ErrorMessage = "Register cost must be between $0 and $50,000.")]
        public int RegisterCost { get; set; } = 0;

        [Required]
        public DateTime StartTime
        {
            get; set;
        }

        public DateTime? EndTime
        {
            get; set;
        }

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
        }
    }
}
