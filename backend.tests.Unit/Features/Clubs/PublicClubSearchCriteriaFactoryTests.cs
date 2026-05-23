using backend.main.features.clubs;
using backend.main.features.clubs.contracts.requests;
using backend.main.features.clubs.search;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Xunit;

namespace backend.tests.Clubs;

public class PublicClubSearchCriteriaFactoryTests
{
    [Fact]
    public void FromQuery_ShouldNormalizeTextAndKeepFilters()
    {
        var criteria = PublicClubSearchCriteriaFactory.FromQuery(
            "  Chess Club  ",
            ClubType.Academic,
            ClubSortBy.Members,
            2,
            15);

        criteria.Query.Should().Be("Chess Club");
        criteria.ClubType.Should().Be(ClubType.Academic);
        criteria.SortBy.Should().Be(ClubSortBy.Members);
        criteria.Page.Should().Be(2);
        criteria.PageSize.Should().Be(15);
    }

    [Fact]
    public void FromRequest_ShouldMatchFromQuery_ForEquivalentInput()
    {
        var request = new ClubSearchRequest
        {
            Query = "  robotics  ",
            Filters = new ClubSearchFilters
            {
                ClubType = ClubType.Gaming
            },
            SortBy = ClubSortBy.Rating,
            Page = 3,
            PageSize = 25
        };

        var fromRequest = PublicClubSearchCriteriaFactory.FromRequest(request);
        var fromQuery = PublicClubSearchCriteriaFactory.FromQuery(
            request.Query,
            request.Filters!.ClubType,
            request.SortBy,
            request.Page,
            request.PageSize);

        fromRequest.Should().BeEquivalentTo(fromQuery);
    }

    [Fact]
    public void FromQuery_ShouldRejectInvalidPageSize()
    {
        var act = () => PublicClubSearchCriteriaFactory.FromQuery(
            "music",
            null,
            ClubSortBy.Relevance,
            1,
            101);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("pageSize must be between 1 and 100.");
    }
}
