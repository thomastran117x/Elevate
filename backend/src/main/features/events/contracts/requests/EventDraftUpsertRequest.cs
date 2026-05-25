using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace backend.main.features.events.contracts.requests
{
    /// <summary>
    /// Partial event payload used when creating or updating drafts.
    /// </summary>
    public sealed class EventDraftUpsertRequest : IValidatableObject
    {
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

        public List<string>? ImageUrls
        {
            get; set;
        }

        public bool? IsPrivate
        {
            get; set;
        }

        [Range(0, 10_000, ErrorMessage = "Max participants must be between 0 and 10,000.")]
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

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartTime.HasValue && StartTime.Value < DateTime.UtcNow)
            {
                yield return new ValidationResult(
                    "StartTime must be in the future.",
                    new[] { nameof(StartTime) });
            }

            if (StartTime.HasValue && EndTime.HasValue && EndTime <= StartTime)
            {
                yield return new ValidationResult(
                    "EndTime must be later than StartTime.",
                    new[] { nameof(EndTime), nameof(StartTime) });
            }

            if (IsPrivate == true && RegisterCost.HasValue && RegisterCost.Value > 0)
            {
                yield return new ValidationResult(
                    "Private events cannot require a registration fee.",
                    new[] { nameof(RegisterCost), nameof(IsPrivate) });
            }

            if (Latitude.HasValue != Longitude.HasValue)
            {
                yield return new ValidationResult(
                    "Latitude and Longitude must both be provided, or both omitted.",
                    new[] { nameof(Latitude), nameof(Longitude) });
            }

            if (ImageUrls != null)
            {
                if (ImageUrls.Count > 5)
                {
                    yield return new ValidationResult(
                        "A maximum of 5 images are allowed.",
                        new[] { nameof(ImageUrls) });
                }

                foreach (var url in ImageUrls)
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    {
                        yield return new ValidationResult(
                            $"'{url}' is not a valid HTTPS URL.",
                            new[] { nameof(ImageUrls) });
                    }
                }
            }

            if (Tags != null)
            {
                if (Tags.Count > 10)
                {
                    yield return new ValidationResult(
                        "A maximum of 10 tags are allowed.",
                        new[] { nameof(Tags) });
                }

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
