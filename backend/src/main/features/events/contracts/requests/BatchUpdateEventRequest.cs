using System.ComponentModel.DataAnnotations;

using backend.main.features.events;

namespace backend.main.features.events.contracts.requests
{
    public class BatchUpdateEventItem
    {
        [Required]
        public int EventId
        {
            get; set;
        }

        [StringLength(30, MinimumLength = 3)]
        public string? Name
        {
            get; set;
        }

        [StringLength(200, MinimumLength = 10)]
        public string? Description
        {
            get; set;
        }

        [StringLength(50)]
        public string? Location
        {
            get; set;
        }

        public bool? IsPrivate
        {
            get; set;
        }

        [Range(1, 10_000, ErrorMessage = "Max participants must be between 1 and 10,000.")]
        public int? MaxParticipants
        {
            get; set;
        }

        [Range(0, 50_000, ErrorMessage = "Register cost must be between $0 and $50,000.")]
        public int? RegisterCost
        {
            get; set;
        }

        public DateTime? StartTime
        {
            get; set;
        }

        public DateTime? EndTime
        {
            get; set;
        }

        [EnumDataType(typeof(EventCategory))]
        public EventCategory? Category
        {
            get; set;
        }

        [StringLength(100)]
        public string? VenueName
        {
            get; set;
        }

        [StringLength(100)]
        public string? City
        {
            get; set;
        }

        [Range(-90, 90)]
        public double? Latitude
        {
            get; set;
        }

        [Range(-180, 180)]
        public double? Longitude
        {
            get; set;
        }

        public List<string>? Tags
        {
            get; set;
        }
    }

    public class BatchUpdateEventRequest : IValidatableObject
    {
        public List<BatchUpdateEventItem> Events { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Events.Count == 0 || Events.Count > 50)
            {
                yield return new ValidationResult(
                    "Batch size must be between 1 and 50 events.",
                    new[] { nameof(Events) });
            }

            var duplicateIds = Events
                .GroupBy(e => e.EventId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                yield return new ValidationResult(
                    $"Duplicate event IDs are not allowed: {string.Join(", ", duplicateIds)}.",
                    new[] { nameof(Events) });
            }

            for (var index = 0; index < Events.Count; index++)
            {
                if (Events[index].Tags is { Count: > 10 })
                {
                    yield return new ValidationResult(
                        "A maximum of 10 tags are allowed.",
                        new[] { $"{nameof(Events)}[{index}].{nameof(BatchUpdateEventItem.Tags)}" });
                }
            }
        }
    }
}
