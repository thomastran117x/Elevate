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
    public async Task UpdateDraftEvent_ShouldReturnUpdatedManagedEvent()
    {
        var updated = BuildEvent(31, clubId: 4, name: "Updated Draft", lifecycleState: EventLifecycleState.Draft);
        var service = new Mock<IEventsService>();
        service.Setup(s => s.UpdateDraftEvent(31, 7, "Organizer", It.IsAny<EventDraftUpsertRequest>()))
            .ReturnsAsync(updated);

        var controller = CreateController(service.Object);

        var result = await controller.UpdateDraftEvent(new EventDraftUpsertRequest
        {
            Name = "Updated Draft"
        }, 31);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>().Subject;
        response.Data!.Name.Should().Be("Updated Draft");
    }

    [Fact]
    public async Task PublishEvent_ShouldReturnManagedEvent()
    {
        var published = BuildEvent(41, clubId: 4, lifecycleState: EventLifecycleState.Published);
        var service = new Mock<IEventsService>();
        service.Setup(s => s.PublishEvent(41, 7, "Organizer"))
            .ReturnsAsync(published);

        var controller = CreateController(service.Object);

        var result = await controller.PublishEvent(41);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>().Subject;
        response.Data!.LifecycleState.Should().Be(EventLifecycleState.Published);
    }

    [Fact]
    public async Task CancelEvent_ShouldReturnManagedEvent()
    {
        var cancelled = BuildEvent(42, clubId: 4, lifecycleState: EventLifecycleState.Cancelled);
        var service = new Mock<IEventsService>();
        service.Setup(s => s.CancelEvent(42, 7, "Organizer"))
            .ReturnsAsync(cancelled);

        var controller = CreateController(service.Object);

        var result = await controller.CancelEvent(42);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>().Subject;
        response.Data!.LifecycleState.Should().Be(EventLifecycleState.Cancelled);
    }

    [Fact]
    public async Task ArchiveEvent_ShouldReturnManagedEvent()
    {
        var archived = BuildEvent(43, clubId: 4, lifecycleState: EventLifecycleState.Archived);
        var service = new Mock<IEventsService>();
        service.Setup(s => s.ArchiveEvent(43, 7, "Organizer"))
            .ReturnsAsync(archived);

        var controller = CreateController(service.Object);

        var result = await controller.ArchiveEvent(43);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>().Subject;
        response.Data!.LifecycleState.Should().Be(EventLifecycleState.Archived);
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
    public async Task GetEvents_ShouldReturnPagedSearchResults_WithDistanceMetadata()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetEvents(It.IsAny<EventSearchCriteria>()))
            .ReturnsAsync((
                new List<backend.main.features.events.Events> { BuildEvent(51, clubId: 4, name: "Nearby Event") },
                1,
                new Dictionary<int, double> { [51] = 1.25 },
                "elasticsearch"));

        var controller = CreateController(service.Object);

        var result = await controller.GetEvents(
            search: "music",
            city: "Ottawa",
            lat: 45.4215,
            lng: -75.6972,
            radiusKm: 10,
            sortBy: EventSortBy.Distance,
            page: 1,
            pageSize: 20);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<EventResponse>>>().Subject;
        response.Data!.Items.Should().ContainSingle();
        response.Data.Items.Single().DistanceKm.Should().Be(1.25);
        response.Meta.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchEvents_ShouldReturnPagedSearchResults()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetEvents(It.IsAny<EventSearchCriteria>()))
            .ReturnsAsync((
                new List<backend.main.features.events.Events> { BuildEvent(52, clubId: 4, name: "Workshop Night") },
                1,
                new Dictionary<int, double>(),
                "database"));

        var controller = CreateController(service.Object);

        var result = await controller.SearchEvents(new EventSearchRequest
        {
            Query = "workshop",
            Filters = new EventSearchFilters
            {
                City = "Ottawa"
            },
            Page = 1,
            PageSize = 20
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<EventResponse>>>().Subject;
        response.Data!.Items.Single().Name.Should().Be("Workshop Night");
    }

    [Fact]
    public async Task GetEventsBatch_ShouldRejectWhenNoValidIdsAreProvided()
    {
        var controller = CreateController(Mock.Of<IEventsService>());

        var result = await controller.GetEventsBatch("abc, , nope");

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("No valid IDs provided.");
    }

    [Fact]
    public async Task GetEventsBatch_ShouldReturnMappedVisibleEvents()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetVisibleEventsByIds(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 9, 10 })), null, null))
            .ReturnsAsync([
                BuildEvent(9, clubId: 4, name: "Event One"),
                BuildEvent(10, clubId: 4, name: "Event Two")
            ]);

        var controller = CreateController(service.Object, user: new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await controller.GetEventsBatch("9,10");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<EventResponse>>>().Subject;
        response.Data!.Select(item => item.Name).Should().Equal("Event One", "Event Two");
    }

    [Fact]
    public async Task BatchCreateEvents_ShouldReturnCreatedPayload()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.BatchCreateEvents(4, 7, "Organizer", It.IsAny<IEnumerable<BatchCreateEventItem>>()))
            .ReturnsAsync(new BatchCreateResultResponse
            {
                Created =
                [
                    new EventResponse
                    {
                        Id = 61,
                        Name = "Created Event",
                        Description = "Created in batch",
                        Location = "Hall",
                        StartTime = DateTime.UtcNow.AddDays(2),
                        ClubId = 4
                    }
                ]
            });

        var controller = CreateController(service.Object);

        var result = await controller.BatchCreateEvents(new BatchCreateEventRequest
        {
            Events =
            [
                new BatchCreateEventItem
                {
                    Name = "Created Event",
                    Description = "Created in batch",
                    Location = "Hall",
                    ImageUrls = ["https://cdn.test/created.png"],
                    StartTime = DateTime.UtcNow.AddDays(2),
                    Category = EventCategory.Social
                }
            ]
        }, 4);

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<BatchCreateResultResponse>>().Subject;
        response.Data!.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task BatchUpdateEvents_ShouldReturnUpdatedCount()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.BatchUpdateEvents(7, "Organizer", It.IsAny<IEnumerable<BatchUpdateEventItem>>()))
            .ReturnsAsync(2);

        var controller = CreateController(service.Object);

        var result = await controller.BatchUpdateEvents(new BatchUpdateEventRequest
        {
            Events =
            [
                new BatchUpdateEventItem { EventId = 1, Name = "One" },
                new BatchUpdateEventItem { EventId = 2, Name = "Two" }
            ]
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("2 event(s) updated successfully.");
    }

    [Fact]
    public async Task BatchDeleteEvents_ShouldReturnDeletedCount()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.BatchDeleteEvents(7, "Organizer", It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(3);

        var controller = CreateController(service.Object);

        var result = await controller.BatchDeleteEvents(new BatchDeleteRequest
        {
            Ids = [1, 2, 3]
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("3 event(s) deleted successfully.");
    }

    [Fact]
    public async Task GetEventAnalytics_ShouldReturnAnalyticsPayload()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetEventAnalytics(71, 7, "Organizer"))
            .ReturnsAsync(new EventAnalyticsResponse
            {
                EventId = 71,
                EventName = "Analytics Event",
                LifecycleState = EventLifecycleState.Published,
                RegistrationCount = 45,
                MaxParticipants = 100
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetEventAnalytics(71);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventAnalyticsResponse>>().Subject;
        response.Data!.EventName.Should().Be("Analytics Event");
    }

    [Fact]
    public async Task GetPresignedUploadUrl_ShouldReturnUploadPayload()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GenerateImageUploadUrlAsync(4, 7, "Organizer", "cover.png", "image/png", 99))
            .ReturnsAsync(new PresignedUploadResponse
            {
                UploadUrl = "https://upload.test",
                PublicUrl = "https://public.test",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetPresignedUploadUrl(new PresignedUrlRequest
        {
            ClubId = 4,
            EventId = 99,
            FileName = "cover.png",
            ContentType = "image/png"
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PresignedUploadResponse>>().Subject;
        response.Data!.PublicUrl.Should().Be("https://public.test");
    }

    [Fact]
    public async Task AddAndRemoveEventImage_ShouldReturnExpectedResponses()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.AddEventImageAsync(88, 7, "Organizer", "https://cdn.test/new.png"))
            .ReturnsAsync(new EventImage
            {
                Id = 5,
                EventId = 88,
                ImageUrl = "https://cdn.test/new.png",
                SortOrder = 1,
                Event = BuildEvent(88, 4)
            });
        service.Setup(s => s.RemoveEventImageAsync(88, 5, 7, "Organizer"))
            .Returns(Task.CompletedTask);

        var controller = CreateController(service.Object);

        var addResult = await controller.AddEventImage(new AddEventImageRequest
        {
            ImageUrl = "https://cdn.test/new.png"
        }, 88);

        var created = addResult.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Message.Should().Be("Image added to event 88 successfully.");

        var removeResult = await controller.RemoveEventImage(88, 5);
        var ok = removeResult.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<MessageResponse>()
            .Which.Message.Should().Be("Image 5 removed from event 88 successfully.");
    }

    [Fact]
    public async Task GetClubAnalytics_ShouldReturnClubAnalyticsPayload()
    {
        var service = new Mock<IEventsService>();
        service.Setup(s => s.GetClubAnalytics(4, 7, "Organizer"))
            .ReturnsAsync(new ClubAnalyticsResponse
            {
                ClubId = 4,
                TotalEvents = 8,
                PublishedEvents = 5
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetClubAnalytics(4);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ClubAnalyticsResponse>>().Subject;
        response.Data!.ClubId.Should().Be(4);
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
