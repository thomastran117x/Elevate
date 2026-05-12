using System.ComponentModel.DataAnnotations;

using backend.main.models.enums;

namespace backend.main.features.events.contracts.requests
{
    public sealed record EventSearchRequest : IValidatableObject
    {
        public string? Query { get; init; }
        public EventSearchFilters? Filters { get; init; }
        public EventGeoFilter? Geo { get; init; }
        public EventSortBy SortBy { get; init; } = EventSortBy.Relevance;

        [Range(1, int.MaxValue, ErrorMessage = "page must be at least 1.")]
        public int Page { get; init; } = 1;

        [Range(1, 100, ErrorMessage = "pageSize must be between 1 and 100.")]
        public int PageSize { get; init; } = 20;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Geo != null)
            {
                if (Geo.RadiusKm <= 0 || Geo.RadiusKm > 500)
                    yield return new ValidationResult(
                        "radiusKm must be between 0 (exclusive) and 500.",
                        [nameof(Geo)]);
            }

            if (SortBy == EventSortBy.Distance && Geo == null)
                yield return new ValidationResult(
                    "sortBy=Distance requires a geo filter with lat, lng, and radiusKm.",
                    [nameof(SortBy)]);

            if (Filters?.Tags != null && Filters.Tags.Count > 5)
                yield return new ValidationResult(
                    "A maximum of 5 tags are allowed per query.",
                    [nameof(Filters)]);
        }
    }

    public sealed record EventSearchFilters
    {
        public EventStatus? Status { get; init; }
        public EventCategory? Category { get; init; }
        public List<string>? Tags { get; init; }
        public string? City { get; init; }
        public bool IsPrivate { get; init; } = false;
    }

    public sealed record EventGeoFilter
    {
        [Required]
        [Range(-90, 90)]
        public double Lat { get; init; }

        [Required]
        [Range(-180, 180)]
        public double Lng { get; init; }

        [Required]
        public double RadiusKm { get; init; }
    }
}

