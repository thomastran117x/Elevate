using backend.main.features.events;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.search;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Xunit;

namespace backend.tests.EventSearch;

public class PublicEventSearchCriteriaFactoryTests
{
    [Fact]
    public void FromQuery_ShouldNormalizeTextAndTags()
    {
        var criteria = PublicEventSearchCriteriaFactory.FromQuery(
            "  Spring Gala  ",
            false,
            EventStatus.Upcoming,
            EventCategory.Social,
            " Music , music, Campus ",
            "  Ottawa  ",
            45.4215,
            -75.6972,
            15,
            EventSortBy.Date,
            2,
            10);

        criteria.Query.Should().Be("Spring Gala");
        criteria.City.Should().Be("Ottawa");
        criteria.Tags.Should().Equal("music", "campus");
        criteria.Status.Should().Be(EventStatus.Upcoming);
        criteria.Category.Should().Be(EventCategory.Social);
        criteria.Page.Should().Be(2);
        criteria.PageSize.Should().Be(10);
    }

    [Fact]
    public void FromRequest_ShouldMatchFromQuery_ForEquivalentInput()
    {
        var request = new EventSearchRequest
        {
            Query = "  hack night  ",
            Filters = new EventSearchFilters
            {
                Status = EventStatus.Ongoing,
                Category = EventCategory.Workshop,
                Tags = [" Tech ", "tech", "Community"],
                City = "  Toronto  ",
            },
            Geo = new EventGeoFilter
            {
                Lat = 43.6532,
                Lng = -79.3832,
                RadiusKm = 20,
            },
            SortBy = EventSortBy.Popularity,
            Page = 3,
            PageSize = 25,
        };

        var fromRequest = PublicEventSearchCriteriaFactory.FromRequest(request);
        var fromQuery = PublicEventSearchCriteriaFactory.FromQuery(
            request.Query,
            false,
            request.Filters!.Status,
            request.Filters.Category,
            " Tech , tech, Community ",
            request.Filters.City,
            request.Geo!.Lat,
            request.Geo.Lng,
            request.Geo.RadiusKm,
            request.SortBy,
            request.Page,
            request.PageSize);

        fromRequest.Should().BeEquivalentTo(fromQuery);
    }

    [Fact]
    public void FromQuery_ShouldRejectDistanceSortWithoutCoordinates()
    {
        var act = () => PublicEventSearchCriteriaFactory.FromQuery(
            "music",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EventSortBy.Distance,
            1,
            20);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("sortBy=Distance requires lat and lng.");
    }

    [Fact]
    public void ValidateRequest_ShouldRejectTooManyTagsAndPrivateSearch()
    {
        var request = new EventSearchRequest
        {
            Filters = new EventSearchFilters
            {
                IsPrivate = true,
                Tags = ["one", "two", "three", "four", "five", "six"],
            }
        };

        var errors = PublicEventSearchCriteriaFactory.ValidateRequest(request);

        errors.Should().Contain(error => error.Message == "Private events are not available through the public events endpoint.");
        errors.Should().Contain(error => error.Message == "A maximum of 5 tags are allowed per query.");
    }
}
