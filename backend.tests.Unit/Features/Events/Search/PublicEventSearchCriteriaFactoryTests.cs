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
    public void FromQuery_ShouldRejectPrivateSearch()
    {
        var act = () => PublicEventSearchCriteriaFactory.FromQuery(
            "invite only",
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EventSortBy.Relevance,
            1,
            20);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("Private events are not available through the public events endpoint.");
    }

    [Fact]
    public void FromRequest_ShouldRejectPrivateSearch()
    {
        var request = new EventSearchRequest
        {
            Filters = new EventSearchFilters
            {
                IsPrivate = true
            }
        };

        var act = () => PublicEventSearchCriteriaFactory.FromRequest(request);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("Private events are not available through the public events endpoint.");
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

    [Fact]
    public void ValidateRequest_ShouldRejectPagingAndGeoInputProblems()
    {
        var request = new EventSearchRequest
        {
            Page = 0,
            PageSize = 101,
            SortBy = EventSortBy.Distance,
            Geo = new EventGeoFilter
            {
                Lat = 91,
                RadiusKm = 501
            }
        };

        var errors = PublicEventSearchCriteriaFactory.ValidateRequest(request);

        errors.Should().Contain(error => error.Message == "page must be at least 1.");
        errors.Should().Contain(error => error.Message == "pageSize must be between 1 and 100.");
        errors.Should().Contain(error => error.Message == "Both lat and lng must be provided together.");
        errors.Should().Contain(error => error.Message == "lat must be between -90 and 90.");
        errors.Should().Contain(error => error.Message == "radiusKm must be between 0 (exclusive) and 500.");
        errors.Should().Contain(error => error.Message == "sortBy=Distance requires lat and lng.");
    }

    [Fact]
    public void ValidateRequest_ShouldRejectInvalidLongitude()
    {
        var request = new EventSearchRequest
        {
            Geo = new EventGeoFilter
            {
                Lat = 45,
                Lng = 181,
                RadiusKm = 10
            }
        };

        var errors = PublicEventSearchCriteriaFactory.ValidateRequest(request);

        errors.Should().Contain(error => error.Message == "lng must be between -180 and 180.");
    }

    [Fact]
    public void ValidateRequest_ShouldAcceptWellFormedDistanceQuery()
    {
        var request = new EventSearchRequest
        {
            Query = " music ",
            Filters = new EventSearchFilters
            {
                Tags = [" Tech ", "tech", "Community"]
            },
            Geo = new EventGeoFilter
            {
                Lat = 45.4215,
                Lng = -75.6972,
                RadiusKm = 15
            },
            SortBy = EventSortBy.Distance,
            Page = 1,
            PageSize = 20
        };

        PublicEventSearchCriteriaFactory.ValidateRequest(request).Should().BeEmpty();
    }

    [Fact]
    public void FromQuery_ShouldRejectInvalidPaging()
    {
        var act = () => PublicEventSearchCriteriaFactory.FromQuery(
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EventSortBy.Relevance,
            0,
            20);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("page must be at least 1.");
    }

    [Fact]
    public void FromQuery_ShouldNormalizeEmptyTagString_ToNull()
    {
        var criteria = PublicEventSearchCriteriaFactory.FromQuery(
            null,
            false,
            null,
            null,
            " , , ",
            "  ",
            null,
            null,
            null,
            EventSortBy.Relevance,
            1,
            20);

        criteria.Tags.Should().BeNull();
        criteria.Query.Should().BeNull();
        criteria.City.Should().BeNull();
        criteria.IsPrivate.Should().BeFalse();
        criteria.LifecycleState.Should().Be(EventLifecycleState.Published);
    }
}
