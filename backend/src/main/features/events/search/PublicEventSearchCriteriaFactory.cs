using backend.main.features.events.contracts.requests;
using backend.main.shared.exceptions.http;

namespace backend.main.features.events.search
{
    public static class PublicEventSearchCriteriaFactory
    {
        public static EventSearchCriteria FromQuery(
            string? search,
            bool isPrivate,
            EventStatus? status,
            EventCategory? category,
            string? tags,
            string? city,
            double? lat,
            double? lng,
            double? radiusKm,
            EventSortBy sortBy,
            int page,
            int pageSize)
        {
            var normalizedTags = NormalizeTagString(tags);

            return CreateOrThrow(new PublicEventSearchInput
            {
                Query = search,
                IsPrivate = isPrivate,
                Status = status,
                Category = category,
                Tags = normalizedTags,
                City = city,
                Lat = lat,
                Lng = lng,
                RadiusKm = radiusKm,
                SortBy = sortBy,
                Page = page,
                PageSize = pageSize
            });
        }

        public static EventSearchCriteria FromRequest(EventSearchRequest request) =>
            CreateOrThrow(new PublicEventSearchInput
            {
                Query = request.Query,
                IsPrivate = request.Filters?.IsPrivate ?? false,
                Status = request.Filters?.Status,
                Category = request.Filters?.Category,
                Tags = NormalizeTags(request.Filters?.Tags),
                City = request.Filters?.City,
                Lat = request.Geo?.Lat,
                Lng = request.Geo?.Lng,
                RadiusKm = request.Geo?.RadiusKm,
                SortBy = request.SortBy,
                Page = request.Page,
                PageSize = request.PageSize
            });

        public static IReadOnlyList<(string Message, string MemberName)> ValidateRequest(EventSearchRequest request)
        {
            var errors = ValidateInput(new PublicEventSearchInput
            {
                Query = request.Query,
                IsPrivate = request.Filters?.IsPrivate ?? false,
                Status = request.Filters?.Status,
                Category = request.Filters?.Category,
                Tags = NormalizeTags(request.Filters?.Tags),
                City = request.Filters?.City,
                Lat = request.Geo?.Lat,
                Lng = request.Geo?.Lng,
                RadiusKm = request.Geo?.RadiusKm,
                SortBy = request.SortBy,
                Page = request.Page,
                PageSize = request.PageSize
            });

            return errors;
        }

        private static EventSearchCriteria CreateOrThrow(PublicEventSearchInput input)
        {
            var errors = ValidateInput(input);
            if (errors.Count > 0)
                throw new BadRequestException(errors[0].Message);

            return new EventSearchCriteria
            {
                Query = NormalizeText(input.Query),
                IsPrivate = input.IsPrivate,
                Status = input.Status,
                Category = input.Category,
                Tags = input.Tags,
                City = NormalizeText(input.City),
                Lat = input.Lat,
                Lng = input.Lng,
                RadiusKm = input.RadiusKm,
                SortBy = input.SortBy,
                Page = input.Page,
                PageSize = input.PageSize
            };
        }

        private static List<(string Message, string MemberName)> ValidateInput(PublicEventSearchInput input)
        {
            var errors = new List<(string Message, string MemberName)>();

            if (input.IsPrivate)
            {
                errors.Add(("Private events are not available through the public events endpoint.", nameof(input.IsPrivate)));
            }

            if (input.Page < 1)
            {
                errors.Add(("page must be at least 1.", nameof(input.Page)));
            }

            if (input.PageSize < 1 || input.PageSize > 100)
            {
                errors.Add(("pageSize must be between 1 and 100.", nameof(input.PageSize)));
            }

            if (input.Lat.HasValue != input.Lng.HasValue)
            {
                errors.Add(("Both lat and lng must be provided together.", nameof(input.Lat)));
            }

            if (input.Lat is < -90 or > 90)
            {
                errors.Add(("lat must be between -90 and 90.", nameof(input.Lat)));
            }

            if (input.Lng is < -180 or > 180)
            {
                errors.Add(("lng must be between -180 and 180.", nameof(input.Lng)));
            }

            if (input.RadiusKm.HasValue && (input.RadiusKm <= 0 || input.RadiusKm > 500))
            {
                errors.Add(("radiusKm must be between 0 (exclusive) and 500.", nameof(input.RadiusKm)));
            }

            if (input.SortBy == EventSortBy.Distance && (!input.Lat.HasValue || !input.Lng.HasValue))
            {
                errors.Add(("sortBy=Distance requires lat and lng.", nameof(input.SortBy)));
            }

            if (input.Tags != null && input.Tags.Count > 5)
            {
                errors.Add(("A maximum of 5 tags are allowed per query.", nameof(input.Tags)));
            }

            return errors;
        }

        private static string? NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private static List<string>? NormalizeTagString(string? tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return null;

            return NormalizeTags(tags.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string>? NormalizeTags(IEnumerable<string>? tags)
        {
            if (tags == null)
                return null;

            var normalized = tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            return normalized.Count == 0 ? null : normalized;
        }

        private sealed record PublicEventSearchInput
        {
            public string? Query { get; init; }
            public bool IsPrivate { get; init; }
            public EventStatus? Status { get; init; }
            public EventCategory? Category { get; init; }
            public List<string>? Tags { get; init; }
            public string? City { get; init; }
            public double? Lat { get; init; }
            public double? Lng { get; init; }
            public double? RadiusKm { get; init; }
            public EventSortBy SortBy { get; init; } = EventSortBy.Relevance;
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
    }
}
