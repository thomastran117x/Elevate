using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class BatchUpdateEventItem
    {
        [Required]
        public int EventId { get; set; }

        [StringLength(30, MinimumLength = 3)]
        public string? Name { get; set; }

        [StringLength(200, MinimumLength = 10)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? Location { get; set; }

        public bool? IsPrivate { get; set; }

        [Range(1, 10_000, ErrorMessage = "Max participants must be between 1 and 10,000.")]
        public int? MaxParticipants { get; set; }

        [Range(0, 50_000, ErrorMessage = "Register cost must be between $0 and $50,000.")]
        public int? RegisterCost { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }
    }

    public class BatchUpdateEventRequest : IValidatableObject
    {
        [Required]
        public List<BatchUpdateEventItem> Events { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Events.Count == 0 || Events.Count > 50)
            {
                yield return new ValidationResult(
                    "Batch size must be between 1 and 50 events.",
                    new[] { nameof(Events) });
            }
        }
    }
}
