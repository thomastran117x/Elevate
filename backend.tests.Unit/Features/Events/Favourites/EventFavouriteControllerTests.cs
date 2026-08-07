using System.Security.Claims;

using backend.main.features.events.favourites;
using backend.main.features.events.favourites.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Events.Favourites;

public class EventFavouriteControllerTests
{
    [Fact]
    public async Task Favourite_ShouldReturnCreatedEnvelope()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.FavouriteAsync(9, 7, "Organizer"))
            .ReturnsAsync(new EventFavouriteResponse
            {
                EventId = 9,
                IsFavourited = true,
                FavouritedAtUtc = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc)
            });

        var controller = CreateController(favouriteService.Object);

        var result = await controller.Favourite(9);

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<EventFavouriteResponse>>().Subject;
        response.Message.Should().Contain("Event with ID 9 has been saved to your favourites.");
        response.Data!.IsFavourited.Should().BeTrue();
        favouriteService.Verify(service => service.FavouriteAsync(9, 7, "Organizer"), Times.Once);
    }

    [Fact]
    public async Task Unfavourite_ShouldReturnOkMessage()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        var controller = CreateController(favouriteService.Object);

        var result = await controller.Unfavourite(9);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<MessageResponse>()
            .Which.Message.Should().Contain("Event with ID 9 has been removed from your favourites.");
        favouriteService.Verify(service => service.UnfavouriteAsync(9, 7), Times.Once);
    }

    [Fact]
    public async Task GetMyStatus_ShouldReportFavouriteState()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.IsFavouritedAsync(9, 7)).ReturnsAsync(true);

        var controller = CreateController(favouriteService.Object);

        var result = await controller.GetMyStatus(9);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventFavouriteResponse>>().Subject;
        response.Data!.EventId.Should().Be(9);
        response.Data.IsFavourited.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyStatus_ShouldOmitTheTimestamp_WhenNotFavourited()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.IsFavouritedAsync(9, 7)).ReturnsAsync(false);

        var controller = CreateController(favouriteService.Object);

        var result = await controller.GetMyStatus(9);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventFavouriteResponse>>().Subject;
        response.Data!.IsFavourited.Should().BeFalse();
        // Not DateTime.MinValue: an unstarred event has no favourited-at date, and serializing
        // year 0001 would contradict the client's nullable contract.
        response.Data.FavouritedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetMyFavouriteIds_ShouldReturnTheIdSet()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.GetFavouriteEventIdsAsync(7))
            .ReturnsAsync([12, 47, 88]);

        var controller = CreateController(favouriteService.Object);

        var result = await controller.GetMyFavouriteIds();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<IEnumerable<int>>>()
            .Which.Data.Should().BeEquivalentTo([12, 47, 88]);
    }

    [Fact]
    public async Task GetMyPinned_ShouldReturnTheUnionRows()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.GetMyPinnedAsync(7, "Organizer"))
            .ReturnsAsync(
            [
                new PinnedEventResponse
                {
                    IsRegistered = true,
                    IsFavourited = false,
                    Event = new main.features.events.contracts.responses.EventResponse { Id = 9 }
                }
            ]);

        var controller = CreateController(favouriteService.Object);

        var result = await controller.GetMyPinned();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<PinnedEventResponse>>>().Subject;
        response.Message.Should().Contain("Your pinned events have been fetched successfully.");
        response.Data.Should().ContainSingle().Which.IsRegistered.Should().BeTrue();
    }

    [Fact]
    public async Task Favourite_ShouldResolveAppExceptions_ToTheirStatusCode()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.FavouriteAsync(9, 7, "Organizer"))
            .ThrowsAsync(new ResourceNotFoundException("Event 9 not found"));

        var controller = CreateController(favouriteService.Object);

        var result = await controller.Favourite(9);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetMyPinned_ShouldResolveUnexpectedExceptions_To500()
    {
        var favouriteService = new Mock<IEventFavouriteService>();
        favouriteService.Setup(service => service.GetMyPinnedAsync(7, "Organizer"))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var controller = CreateController(favouriteService.Object);

        var result = await controller.GetMyPinned();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    private static EventFavouriteController CreateController(IEventFavouriteService favouriteService)
    {
        var controller = new EventFavouriteController(favouriteService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "organizer@example.com"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
