using backend.main.features.clubs.contracts.requests;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.search
{
    public static class PublicClubSearchCriteriaFactory
    {
        public static ClubSearchCriteria FromQuery(
            string? search,
            ClubType? clubType,
            ClubSortBy sortBy,
            int page,
            int pageSize) =>
            CreateOrThrow(new PublicClubSearchInput
            {
                Query = search,
                ClubType = clubType,
                SortBy = sortBy,
                Page = page,
                PageSize = pageSize
            });

        public static ClubSearchCriteria FromRequest(ClubSearchRequest request) =>
            CreateOrThrow(new PublicClubSearchInput
            {
                Query = request.Query,
                ClubType = request.Filters?.ClubType,
                SortBy = request.SortBy,
                Page = request.Page,
                PageSize = request.PageSize
            });

        public static IReadOnlyList<(string Message, string MemberName)> ValidateRequest(ClubSearchRequest request) =>
            ValidateInput(new PublicClubSearchInput
            {
                Query = request.Query,
                ClubType = request.Filters?.ClubType,
                SortBy = request.SortBy,
                Page = request.Page,
                PageSize = request.PageSize
            });

        private static ClubSearchCriteria CreateOrThrow(PublicClubSearchInput input)
        {
            var errors = ValidateInput(input);
            if (errors.Count > 0)
                throw new BadRequestException(errors[0].Message);

            return new ClubSearchCriteria
            {
                Query = NormalizeText(input.Query),
                ClubType = input.ClubType,
                SortBy = input.SortBy,
                Page = input.Page,
                PageSize = input.PageSize
            };
        }

        private static List<(string Message, string MemberName)> ValidateInput(PublicClubSearchInput input)
        {
            var errors = new List<(string Message, string MemberName)>();

            if (input.Page < 1)
            {
                errors.Add(("page must be at least 1.", nameof(input.Page)));
            }

            if (input.PageSize < 1 || input.PageSize > 100)
            {
                errors.Add(("pageSize must be between 1 and 100.", nameof(input.PageSize)));
            }

            return errors;
        }

        private static string? NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private sealed record PublicClubSearchInput
        {
            public string? Query { get; init; }
            public ClubType? ClubType { get; init; }
            public ClubSortBy SortBy { get; init; } = ClubSortBy.Relevance;
            public int Page { get; init; } = 1;
            public int PageSize { get; init; } = 20;
        }
    }
}
