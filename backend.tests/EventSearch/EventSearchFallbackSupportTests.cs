using backend.main.features.events;
using backend.main.features.events.search;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Xunit;

namespace backend.tests.EventSearch;

public class EventSearchFallbackSupportTests
{
    [Fact]
    public void EnsureSupported_ShouldAllowCoreFilters()
    {
        var act = () => EventSearchFallbackSupport.EnsureSupported(new EventSearchCriteria
        {
            Query = "hack night",
            Category = EventCategory.Workshop,
            Status = EventStatus.Upcoming,
            SortBy = EventSortBy.Date,
            Page = 1,
            PageSize = 20,
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSupported_ShouldRejectTagFiltering()
    {
        var act = () => EventSearchFallbackSupport.EnsureSupported(new EventSearchCriteria
        {
            Tags = ["music"],
            Page = 1,
            PageSize = 20,
        });

        act.Should()
            .Throw<NotAvailableException>()
            .WithMessage("Tag filtering is temporarily unavailable because search indexing is unavailable.");
    }

    [Fact]
    public void EnsureSupported_ShouldRejectGeoFiltering()
    {
        var act = () => EventSearchFallbackSupport.EnsureSupported(new EventSearchCriteria
        {
            Lat = 45.4215,
            Lng = -75.6972,
            RadiusKm = 10,
            Page = 1,
            PageSize = 20,
        });

        act.Should()
            .Throw<NotAvailableException>()
            .WithMessage("Location-based filtering is temporarily unavailable because search indexing is unavailable.");
    }

    [Fact]
    public void EnsureSupported_ShouldRejectDistanceSorting()
    {
        var act = () => EventSearchFallbackSupport.EnsureSupported(new EventSearchCriteria
        {
            SortBy = EventSortBy.Distance,
            Page = 1,
            PageSize = 20,
        });

        act.Should()
            .Throw<NotAvailableException>()
            .WithMessage("Distance sorting is temporarily unavailable because search indexing is unavailable.");
    }
}
