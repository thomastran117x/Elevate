using System.Security.Claims;

using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.events.versions.contracts.responses;
using backend.main.features.events.images;
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
    public async Task CreateEvent_ShouldReturnCreatedEventResponse()
    {
        var created = BuildEvent(15, clubId: 4, name: "Campus Mixer");
        var service = new Mock<IEventsService>();
        service.Setup(s => s.CreateEvent(
                4,
                7,
                "Organizer",
                "Campus Mixer",
                "A welcoming social event for students.",
                "Student Center",
                It.IsAny<List<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime?>(),
                false,
                120,
                0,
                EventCategory.Social,
                "Main Hall",
                "Ottawa",
                45.4215,
                -75.6972,
                It.IsAny<List<string>?>()))
            .ReturnsAsync(created);

        var controller = CreateController(service.Object);

        var result = await controller.CreateEvent(new EventCreateRequest
        {
            Name = "Campus Mixer",
            Description = "A welcoming social event for students.",
            Location = "Student Center",
            ImageUrls = ["https://cdn.test/cover.png"],
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            MaxParticipants = 120,
            RegisterCost = 0,
            Category = EventCategory.Social,
            VenueName = "Main Hall",
            City = "Ottawa",
            Latitude = 45.4215,
            Longitude = -75.6972,
            Tags = ["campus"]
        }, 4);

        var createdResult = result.Should().BeOfType<ObjectResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var response = createdResult.Value.Should().BeOfType<ApiResponse<EventResponse>>().Subject;
        response.Data!.Id.Should().Be(15);
        response.Data.Name.Should().Be("Campus Mixer");
    }

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

    [Fact]
    public async Task GetEventsByClub_ShouldRejectInvalidPage()
    {
        var controller = CreateController(Mock.Of<IEventsService>());

        var result = await controller.GetEventsByClub(4, page: 0, pageSize: 20);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("page must be at least 1.");
    }

    [Fact]
    public async Task GetEventsByClub_ShouldReturnPagedResults()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetEventsByClub(4, EventStatus.Upcoming, 1, 10))
            .ReturnsAsync((
                new List<backend.main.features.events.Events> { BuildEvent(21, clubId: 4, name: "Upcoming Night") },
                1,
                "database"));

        var controller = CreateController(service.Object);

        var result = await controller.GetEventsByClub(4, EventStatus.Upcoming, 1, 10);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<EventResponse>>>().Subject;
        response.Data!.Items.Should().ContainSingle();
        response.Meta.Should().NotBeNull();
    }

    [Fact]
    public async Task GetManageableEventsByClub_ShouldRejectInvalidPageSize()
    {
        var controller = CreateController(Mock.Of<IEventsService>());

        var result = await controller.GetManageableEventsByClub(4, page: 1, pageSize: 101);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("pageSize must be between 1 and 100.");
    }

    [Fact]
    public async Task CreateDraftEvent_ShouldReturnCreatedManagedEvent()
    {
        var created = BuildEvent(30, clubId: 4, lifecycleState: EventLifecycleState.Draft);
        var service = new Mock<IEventsService>();
        service.Setup(s => s.CreateDraftEvent(4, 7, "Organizer", It.IsAny<EventDraftUpsertRequest>()))
            .ReturnsAsync(created);

        var controller = CreateController(service.Object);

        var result = await controller.CreateDraftEvent(new EventDraftUpsertRequest
        {
            Name = "Draft Event",
            Description = "Draft description",
            Location = "Room 201"
        }, 4);

        var createdResult = result.Should().BeOfType<ObjectResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var response = createdResult.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>().Subject;
        response.Data!.Id.Should().Be(30);
        response.Data.LifecycleState.Should().Be(EventLifecycleState.Draft);
    }

    [Fact]
    public async Task GetEvent_ShouldAttachClubPayload()
    {
        var eventService = new Mock<IEventsService>();
        eventService.Setup(s => s.GetVisibleEvent(9, It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync(BuildEvent(9, clubId: 4, name: "Board Game Night"));

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4))
            .ReturnsAsync(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Games Club",
                Description = "Tabletop community",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/games.png",
                MemberCount = 25,
                EventCount = 5,
                AvaliableEventCount = 3
            });

        var controller = CreateController(eventService.Object, clubService.Object, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await controller.GetEvent(9);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventResponse>>().Subject;
        response.Data!.Club.Should().NotBeNull();
        response.Data.Club!.Name.Should().Be("Games Club");
    }

    [Fact]
    public async Task AdminEventsController_ReindexEvents_ShouldReturnIndexedCount()
    {
        var reindexService = new Mock<IEventReindexService>();
        reindexService.Setup(service => service.ReindexAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);

        var controller = new AdminEventsController(reindexService.Object);

        var result = await controller.ReindexEvents(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        response.Message.Should().Be("Events reindexed successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.ToString().Should().Contain("12");
    }

    private static EventsController CreateController(
        IEventsService service,
        IClubService? clubService = null,
        ClaimsPrincipal? user = null)
    {
        var controller = new EventsController(service, clubService ?? Mock.Of<IClubService>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "owner@test.local"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }

    private static backend.main.features.events.Events BuildEvent(
        int id,
        int clubId,
        string name = "Event Name",
        EventLifecycleState lifecycleState = EventLifecycleState.Published)
    {
        return new backend.main.features.events.Events
        {
            Id = id,
            Name = name,
            Description = "A detailed event description.",
            Location = "Student Center",
            StartTime = DateTime.UtcNow.AddDays(3),
            EndTime = DateTime.UtcNow.AddDays(3).AddHours(2),
            ClubId = clubId,
            CurrentVersionNumber = 2,
            LifecycleState = lifecycleState,
            Category = EventCategory.Social,
            Images = [new EventImage { ImageUrl = "https://cdn.test/events/cover.png", SortOrder = 0 }]
        };
    }
}
