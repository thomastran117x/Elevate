using System.ComponentModel.DataAnnotations;

using backend.main.features.events;
using backend.main.features.events.search;

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
            foreach (var error in PublicEventSearchCriteriaFactory.ValidateRequest(this))
            {
                yield return new ValidationResult(error.Message, [error.MemberName]);
            }
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
        [Range(-90, 90, ErrorMessage = "lat must be between -90 and 90.")]
        public double? Lat { get; init; }

        [Range(-180, 180, ErrorMessage = "lng must be between -180 and 180.")]
        public double? Lng { get; init; }

        public double? RadiusKm { get; init; }
    }
}


