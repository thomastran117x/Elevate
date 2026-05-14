using System.Security.Claims;

using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.versions;
using backend.main.features.events.versions.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using Xunit;

namespace backend.tests.Events;

public class EventControllerTests
{
    [Fact]
    public async Task GetEventVersions_ShouldReturnPagedHistoryResponse()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetVersionHistoryAsync(9, 7, "Organizer", 1, 20))
            .ReturnsAsync((
                new List<EventVersionHistoryItem>
                {
                    new(
                        9,
                        2,
                        EventVersionActions.Update,
                        new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
                        7,
                        "Organizer",
                        true,
                        new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc),
                        null,
                        [new EventVersionFieldChange
                        {
                            Field = "name",
                            OldValue = "Board Game Night",
                            NewValue = "Advanced Board Game Night"
                        }])
                },
                1));

        var controller = CreateController(service.Object);

        var result = await controller.GetEventVersions(9, 1, 20);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<EventVersionListItemResponse>>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().ContainSingle();
        response.Data.Items.Single().VersionNumber.Should().Be(2);
        response.Data.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task RollbackEventVersion_ShouldReturnRollbackPayload()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.RollbackToVersionAsync(9, 1, 7, "Organizer"))
            .ReturnsAsync(new EventRollbackResult(
                new backend.main.features.events.Events
                {
                    Id = 9,
                    Name = "Board Game Night",
                    Description = "A casual game night with strategy tables.",
                    Location = "Student Center",
                    StartTime = new DateTime(2026, 5, 20, 19, 0, 0, DateTimeKind.Utc),
                    EndTime = new DateTime(2026, 5, 20, 22, 0, 0, DateTimeKind.Utc),
                    ClubId = 4,
                    CurrentVersionNumber = 3,
                    Category = EventCategory.Gaming
                },
                1,
                3));

        var controller = CreateController(service.Object);

        var result = await controller.RollbackEventVersion(9, 1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventRollbackResponse>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.RestoredFromVersionNumber.Should().Be(1);
        response.Data.NewVersionNumber.Should().Be(3);
        response.Data.Event.CurrentVersionNumber.Should().Be(3);
    }

    private static EventsController CreateController(IEventsService service)
    {
        var controller = new EventsController(service, Mock.Of<IClubService>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "owner@test.local"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
