using System.Reflection;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.analytics;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Events;

public class EventsServiceTests
{
    [Fact]
    public async Task GetVisibleEvent_ShouldReturnPublicEvent_ForAnonymousViewer()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(isPrivate: false);
        harness.SetupCachedEvent(ev);

        var visible = await harness.Service.GetVisibleEvent(ev.Id);

        visible.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task GetVisibleEvent_ShouldAllowPrivateEvent_ForRegisteredUser()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(isPrivate: true);
        harness.SetupCachedEvent(ev);
        harness.RegistrationRepositoryMock
            .Setup(repository => repository.IsRegisteredAsync(ev.Id, harness.ViewerUserId))
            .ReturnsAsync(new EventRegistration { EventId = ev.Id, UserId = harness.ViewerUserId });

        var visible = await harness.Service.GetVisibleEvent(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        visible.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task GetVisibleEvent_ShouldAllowPrivateEvent_ForPendingPayer()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(isPrivate: true);
        harness.SetupCachedEvent(ev);
        harness.Db.Events.Add(ev);
        await harness.Db.SaveChangesAsync();
        harness.Db.Payments.Add(new backend.main.features.payment.Payment
        {
            EventId = ev.Id,
            UserId = harness.ViewerUserId,
            Amount = 1500,
            Status = PaymentStatus.Pending
        });
        await harness.Db.SaveChangesAsync();

        var visible = await harness.Service.GetVisibleEvent(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        visible.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task EnsureCanViewEventAsync_ShouldThrowNotFound_ForUnauthorizedPrivateViewer()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(isPrivate: true);
        harness.SetupCachedEvent(ev);

        var action = () => harness.Service.EnsureCanViewEventAsync(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Event {ev.Id} not found");
    }

    [Fact]
    public async Task GetEvents_ShouldReturnElasticsearchResultsInHitOrder_AndNormalizeQuery()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        EventSearchCriteria? capturedCriteria = null;
        harness.SearchServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .Callback<EventSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(new EventSearchResult(
                [
                    new EventSearchHit(22, 3.2),
                    new EventSearchHit(11, null)
                ],
                2));

        var second = harness.BuildEvent(id: 22, name: "Second");
        var first = harness.BuildEvent(id: 11, name: "First");
        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([first, second]);

        var result = await harness.Service.GetEvents(new EventSearchCriteria
        {
            Query = "  Board Games  ",
            Page = 1,
            PageSize = 20
        });

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Query.Should().Be("board games");
        result.Source.Should().Be(ResponseSource.Elasticsearch);
        result.Events.Select(ev => ev.Id).Should().Equal(22, 11);
        result.DistanceKmById.Should().ContainKey(22).WhoseValue.Should().Be(3.2);
        result.DistanceKmById.Should().NotContainKey(11);
    }

    [Fact]
    public async Task GetEvents_ShouldFallbackToDatabase_WhenElasticsearchIsUnavailable()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        EventSearchCriteria? capturedCriteria = null;
        harness.SearchServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .ThrowsAsync(new ElasticsearchUnavailableException("search unavailable"));

        var expected = harness.BuildEvent(id: 33, name: "Fallback Event");
        harness.EventsRepositoryMock
            .Setup(repository => repository.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .Callback<EventSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(([expected], 1));

        var result = await harness.Service.GetEvents(new EventSearchCriteria
        {
            Query = "  Fallback Query  ",
            SortBy = EventSortBy.Date,
            Page = 2,
            PageSize = 10
        });

        result.Source.Should().Be(ResponseSource.Database);
        result.Events.Should().ContainSingle().Which.Id.Should().Be(33);
        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Query.Should().Be("fallback query");
        capturedCriteria.SortBy.Should().Be(EventSortBy.Date);
        capturedCriteria.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetEvents_ShouldRejectUnsupportedFallbackFilters_WhenElasticsearchIsUnavailable()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.SearchServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .ThrowsAsync(new ElasticsearchUnavailableException("search unavailable"));

        var action = () => harness.Service.GetEvents(new EventSearchCriteria
        {
            Tags = ["music"],
            Page = 1,
            PageSize = 20
        });

        await action.Should()
            .ThrowAsync<NotAvailableException>()
            .WithMessage("Tag filtering is temporarily unavailable because search indexing is unavailable.");
    }

    [Fact]
    public async Task GenerateImageUploadUrlAsync_ShouldStoreUploadIntent_AndNormalizeContentType()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var club = new Club
        {
            Id = harness.ClubId,
            UserId = harness.OwnerUserId,
            Name = "Board Games Club",
            Description = "A club for tabletop events.",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/gaming.png"
        };

        harness.ClubServiceMock
            .Setup(service => service.GetClub(harness.ClubId))
            .ReturnsAsync(club);
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);

        var uploadResponse = new PresignedUploadResponse
        {
            UploadUrl = "https://upload.test/signed",
            PublicUrl = "https://cdn.test/events/poster.png",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        harness.BlobServiceMock
            .Setup(service => service.GenerateUploadUrlAsync(
                $"events/clubs/{harness.ClubId}/pending",
                "poster.png",
                " IMAGE/PNG "))
            .ReturnsAsync(uploadResponse);

        string? storedKey = null;
        string? storedPayload = null;
        TimeSpan? storedExpiry = null;
        harness.CacheMock
            .Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((key, payload, expiry) =>
            {
                storedKey = key;
                storedPayload = payload;
                storedExpiry = expiry;
            })
            .ReturnsAsync(true);

        var result = await harness.Service.GenerateImageUploadUrlAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            "poster.png",
            " IMAGE/PNG ");

        result.PublicUrl.Should().Be(uploadResponse.PublicUrl);
        storedKey.Should().NotBeNull();
        storedKey.Should().Contain("event:image-upload:intent:");
        storedExpiry.Should().Be(TimeSpan.FromMinutes(20));

        using var json = JsonDocument.Parse(storedPayload!);
        json.RootElement.GetProperty("ClubId").GetInt32().Should().Be(harness.ClubId);
        json.RootElement.GetProperty("UserId").GetInt32().Should().Be(harness.OwnerUserId);
        json.RootElement.GetProperty("EventId").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("PublicUrl").GetString().Should().Be(uploadResponse.PublicUrl);
        json.RootElement.GetProperty("ContentType").GetString().Should().Be("image/png");
    }

    [Fact]
    public async Task GenerateImageUploadUrlAsync_ShouldThrowNotAvailable_WhenIntentCannotBeStored()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var club = new Club
        {
            Id = harness.ClubId,
            UserId = harness.OwnerUserId,
            Name = "Board Games Club",
            Description = "A club for tabletop events.",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/gaming.png"
        };

        harness.ClubServiceMock
            .Setup(service => service.GetClub(harness.ClubId))
            .ReturnsAsync(club);
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);
        harness.BlobServiceMock
            .Setup(service => service.GenerateUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new PresignedUploadResponse
            {
                UploadUrl = "https://upload.test/signed",
                PublicUrl = "https://cdn.test/events/poster.png",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });
        harness.CacheMock
            .Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);

        var action = () => harness.Service.GenerateImageUploadUrlAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            "poster.png",
            "image/png");

        await action.Should()
            .ThrowAsync<NotAvailableException>()
            .WithMessage("Image uploads are temporarily unavailable. Please try again shortly.");
    }

    [Fact]
    public async Task NotifyRegistrationChangedAsync_ShouldStageSyncAndClearCachedEvent()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.Db.Events.Add(harness.BuildEvent(id: 55, clubId: harness.ClubId));
        await harness.Db.SaveChangesAsync();

        await harness.Service.NotifyRegistrationChangedAsync(55);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(ev => ev.Id == 55)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync("event:55"), Times.Once);
    }

    [Fact]
    public async Task PublishEvent_ShouldTransitionDraftToPublished_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(
            lifecycleState: EventLifecycleState.Draft,
            imageUrls: ["https://cdn.test/events/publish.png"]);
        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdAsync(ev.Id))
            .ReturnsAsync(() => harness.LoadEvent(ev.Id));

        var published = await harness.Service.PublishEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        published.LifecycleState.Should().Be(EventLifecycleState.Published);
        published.CurrentVersionNumber.Should().Be(2);

        var persisted = await harness.Db.Events.SingleAsync(e => e.Id == ev.Id);
        persisted.LifecycleState.Should().Be(EventLifecycleState.Published);
        persisted.CurrentVersionNumber.Should().Be(2);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity =>
                entity.Id == ev.Id &&
                entity.LifecycleState == EventLifecycleState.Published)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"event:{ev.Id}",
            It.IsAny<backend.main.features.events.Events>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);

        var latestVersion = await harness.Db.EventVersions
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();
        latestVersion.ActionType.Should().Be(EventVersionActions.Publish);
    }

    [Fact]
    public async Task PublishEvent_ShouldRejectDraft_WhenPublishRequirementsAreMissing()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(
            lifecycleState: EventLifecycleState.Draft,
            imageUrls: []);

        var action = () => harness.Service.PublishEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        var exception = await action.Should().ThrowAsync<BadRequestException>();
        exception.Which.Message.Should().Contain("This event is not ready to publish.");
        exception.Which.Message.Should().Contain("At least one image");
    }

    [Fact]
    public async Task CancelEvent_ShouldRejectInvalidLifecycleTransition()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(
            lifecycleState: EventLifecycleState.Draft,
            imageUrls: ["https://cdn.test/events/draft-only.png"]);

        var action = () => harness.Service.CancelEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Cannot transition an event from Draft to Cancelled.");
    }

    [Fact]
    public async Task GetManageableEventsByClub_ShouldNormalizePaging_AndUseDateSort()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var club = new Club
        {
            Id = harness.ClubId,
            UserId = harness.OwnerUserId,
            Name = "Board Games Club",
            Description = "A club for tabletop events.",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/gaming.png"
        };

        harness.ClubServiceMock
            .Setup(service => service.GetClub(harness.ClubId))
            .ReturnsAsync(club);
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);

        EventSearchCriteria? capturedCriteria = null;
        harness.EventsRepositoryMock
            .Setup(repository => repository.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .Callback<EventSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(([], 0));

        await harness.Service.GetManageableEventsByClub(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            lifecycleState: EventLifecycleState.Draft,
            page: 0,
            pageSize: 999);

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.ClubId.Should().Be(harness.ClubId);
        capturedCriteria.LifecycleState.Should().Be(EventLifecycleState.Draft);
        capturedCriteria.Page.Should().Be(1);
        capturedCriteria.PageSize.Should().Be(100);
        capturedCriteria.SortBy.Should().Be(EventSortBy.Date);
    }

    [Fact]
    public async Task BatchUpdateEvents_ShouldRejectDuplicateIds()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();

        var action = () => harness.Service.BatchUpdateEvents(
            harness.OwnerUserId,
            harness.OwnerRole,
            [
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem { EventId = 9, Name = "Updated Event One" },
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem { EventId = 9, Name = "Updated Event Two" }
            ]);

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Duplicate event IDs are not allowed: 9.");
    }

    [Fact]
    public async Task BatchDeleteEvents_ShouldDeleteRequestedIds_StageDeletes_AndClearCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var first = harness.BuildEvent(id: 41, clubId: harness.ClubId);
        first.Images.Add(new EventImage { EventId = 41, ImageUrl = "https://cdn.test/events/41.png" });
        var second = harness.BuildEvent(id: 42, clubId: harness.ClubId);
        second.Images.Add(new EventImage { EventId = 42, ImageUrl = "https://cdn.test/events/42.png" });

        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([first, second]);
        harness.ClubServiceMock
            .Setup(service => service.CanManageClubAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);
        harness.EventsRepositoryMock
            .Setup(repository => repository.DeleteManyAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(2);

        var deleted = await harness.Service.BatchDeleteEvents(harness.OwnerUserId, harness.OwnerRole, [41, 42]);

        deleted.Should().Be(2);
        harness.OutboxWriterMock.Verify(writer => writer.StageDelete(41), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageDelete(42), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync("event:41"), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync("event:42"), Times.Once);
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/41.png"), Times.Once);
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/42.png"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task CreateDraftEvent_ShouldTrimFields_NormalizeTags_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();

        var request = new backend.main.features.events.contracts.requests.EventDraftUpsertRequest
        {
            Name = "  Draft Event  ",
            Description = "  A carefully prepared draft event for testing.  ",
            Location = "  Student Center  ",
            VenueName = "  Room 201  ",
            City = "  Toronto  ",
            Tags = ["Games", "games", " Campus "]
        };

        var created = await harness.Service.CreateDraftEvent(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            request);

        created.Name.Should().Be("Draft Event");
        created.Description.Should().Be("A carefully prepared draft event for testing.");
        created.Location.Should().Be("Student Center");
        created.VenueName.Should().Be("Room 201");
        created.City.Should().Be("Toronto");
        created.Tags.Should().Equal("games", "campus");
        created.LifecycleState.Should().Be(EventLifecycleState.Draft);
        created.CurrentVersionNumber.Should().Be(1);

        var version = await harness.Db.EventVersions.SingleAsync(v => v.EventId == created.Id);
        version.ActionType.Should().Be(EventVersionActions.Create);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(ev => ev.Id == created.Id)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"event:{created.Id}",
            It.IsAny<backend.main.features.events.Events>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task UpdateDraftEvent_ShouldReplaceImages_DeleteRemovedBlob_AndNormalizeTags()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        harness.ConfigureImageRepositoryPersistence();

        var existing = await harness.SeedPersistedEventAsync(
            id: 61,
            lifecycleState: EventLifecycleState.Draft,
            imageUrls:
            [
                "https://cdn.test/events/old-1.png",
                "https://cdn.test/events/keep.png"
            ]);

        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdAsync(existing.Id))
            .ReturnsAsync(() => harness.LoadEvent(existing.Id));

        var newUrl = "https://cdn.test/events/new.png";
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = existing.Id,
                UserId = harness.OwnerUserId,
                PublicUrl = newUrl,
                ContentType = "image/png"
            }));

        var updated = await harness.Service.UpdateDraftEvent(
            existing.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            new backend.main.features.events.contracts.requests.EventDraftUpsertRequest
            {
                Name = "  Updated Draft  ",
                ImageUrls =
                [
                    "https://cdn.test/events/keep.png",
                    newUrl
                ],
                Tags = ["Night", " night ", "Campus"]
            });

        updated.Name.Should().Be("Updated Draft");
        updated.Tags.Should().Equal("night", "campus");

        var images = await harness.Db.EventImages
            .Where(image => image.EventId == existing.Id)
            .OrderBy(image => image.SortOrder)
            .Select(image => image.ImageUrl)
            .ToListAsync();
        images.Should().Equal("https://cdn.test/events/keep.png", newUrl);

        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/old-1.png"), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(ev => ev.Id == existing.Id)),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task AddEventImageAsync_ShouldRejectWhenEventAlreadyHasFiveImages()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 71, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);
        harness.ClubServiceMock
            .Setup(service => service.CanManageEventMediaAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);
        harness.ImageRepositoryMock
            .Setup(repository => repository.CountByEventIdAsync(ev.Id))
            .ReturnsAsync(5);
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = ev.Id,
                UserId = harness.OwnerUserId,
                PublicUrl = "https://cdn.test/events/sixth.png",
                ContentType = "image/png"
            }));

        var action = () => harness.Service.AddEventImageAsync(
            ev.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            "https://cdn.test/events/sixth.png");

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("An event cannot have more than 5 images.");
    }

    [Fact]
    public async Task RemoveEventImageAsync_ShouldDeleteImage_ClearCache_AndBumpVersion()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureImageRepositoryPersistence();
        var ev = harness.BuildEvent(id: 81, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);
        harness.Db.Events.Add(ev);
        await harness.Db.SaveChangesAsync();
        harness.ClubServiceMock
            .Setup(service => service.CanManageEventMediaAsync(harness.ClubId, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);

        var image = new EventImage
        {
            Id = 12,
            EventId = ev.Id,
            ImageUrl = "https://cdn.test/events/remove-me.png",
            SortOrder = 0
        };
        harness.Db.EventImages.Add(image);
        await harness.Db.SaveChangesAsync();

        await harness.Service.RemoveEventImageAsync(ev.Id, image.Id, harness.OwnerUserId, harness.OwnerRole);

        (await harness.Db.EventImages.AnyAsync(i => i.Id == image.Id)).Should().BeFalse();
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync(image.ImageUrl), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{ev.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ShouldReturnVersionsInDescendingOrder_AndNormalizePaging()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        await harness.SeedPersistedEventAsync(id: 91, lifecycleState: EventLifecycleState.Published);

        harness.Db.EventVersions.AddRange(
            new EventVersion
            {
                EventId = 91,
                VersionNumber = 1,
                ActionType = EventVersionActions.Create,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ActorUserId = harness.OwnerUserId,
                ActorRole = harness.OwnerRole,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new EventVersion
            {
                EventId = 91,
                VersionNumber = 2,
                ActionType = EventVersionActions.Publish,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ActorUserId = harness.OwnerUserId,
                ActorRole = harness.OwnerRole,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await harness.Db.SaveChangesAsync();

        var (items, totalCount) = await harness.Service.GetVersionHistoryAsync(
            91,
            harness.OwnerUserId,
            harness.OwnerRole,
            page: 0,
            pageSize: 999);

        totalCount.Should().Be(2);
        items.Select(item => item.VersionNumber).Should().Equal(2, 1);
    }

    [Fact]
    public async Task GetVersionDetailAsync_ShouldReturnRequestedVersionDetail()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        await harness.SeedPersistedEventAsync(id: 101, lifecycleState: EventLifecycleState.Published);

        harness.Db.EventVersions.Add(new EventVersion
        {
            EventId = 101,
            VersionNumber = 1,
            ActionType = EventVersionActions.Create,
            SnapshotJson = JsonSerializer.Serialize(new EventVersionSnapshot
            {
                Name = "Event One",
                Description = "Detailed version snapshot for testing.",
                Location = "Student Center",
                IsPrivate = false,
                MaxParticipants = 50,
                RegisterCost = 0,
                StartTime = DateTime.UtcNow.AddDays(5),
                EndTime = DateTime.UtcNow.AddDays(5).AddHours(2),
                ClubId = harness.ClubId,
                LifecycleState = EventLifecycleState.Draft,
                Category = EventCategory.Gaming,
                VenueName = "Room A",
                City = "Toronto",
                Latitude = null,
                Longitude = null,
                Tags = ["games"]
            }),
            ChangedFieldsJson = JsonSerializer.Serialize(new List<EventVersionFieldChange>
            {
                new()
                {
                    Field = "name",
                    OldValue = null,
                    NewValue = "Event One"
                }
            }),
            ActorUserId = harness.OwnerUserId,
            ActorRole = harness.OwnerRole,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        var detail = await harness.Service.GetVersionDetailAsync(
            101,
            1,
            harness.OwnerUserId,
            harness.OwnerRole);

        detail.VersionNumber.Should().Be(1);
        detail.ActionType.Should().Be(EventVersionActions.Create);
        detail.Snapshot.Name.Should().Be("Event One");
        detail.ChangedFields.Should().ContainSingle(change => change.Field == "name");
    }

    [Fact]
    public async Task GetVisibleEventsByIds_ShouldReturnOnlyVisibleEvents_InRequestedOrder()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var publicEvent = harness.BuildEvent(id: 201, clubId: harness.ClubId, name: "Public Event", isPrivate: false);
        var privateVisible = harness.BuildEvent(id: 202, clubId: harness.ClubId, name: "Private Visible", isPrivate: true);
        var privateHidden = harness.BuildEvent(id: 203, clubId: harness.ClubId, name: "Private Hidden", isPrivate: true);

        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([privateHidden, publicEvent, privateVisible]);
        harness.RegistrationRepositoryMock
            .Setup(repository => repository.IsRegisteredAsync(privateVisible.Id, harness.ViewerUserId))
            .ReturnsAsync(new EventRegistration { EventId = privateVisible.Id, UserId = harness.ViewerUserId });

        var visible = await harness.Service.GetVisibleEventsByIds(
            [202, 201, 203, 202],
            harness.ViewerUserId,
            harness.ViewerRole);

        visible.Select(ev => ev.Id).Should().Equal(202, 201);
    }

    [Fact]
    public async Task GetEventsByClub_ShouldUsePublishedPublicCriteria_AndReturnDatabaseSource()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        EventSearchCriteria? capturedCriteria = null;
        harness.EventsRepositoryMock
            .Setup(repository => repository.SearchAsync(It.IsAny<EventSearchCriteria>()))
            .Callback<EventSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(([harness.BuildEvent(id: 211, clubId: harness.ClubId)], 1));

        var result = await harness.Service.GetEventsByClub(harness.ClubId, EventStatus.Upcoming, page: 2, pageSize: 15);

        result.Source.Should().Be(ResponseSource.Database);
        result.Events.Should().ContainSingle().Which.Id.Should().Be(211);
        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.ClubId.Should().Be(harness.ClubId);
        capturedCriteria.IsPrivate.Should().BeFalse();
        capturedCriteria.LifecycleState.Should().Be(EventLifecycleState.Published);
        capturedCriteria.Status.Should().Be(EventStatus.Upcoming);
        capturedCriteria.Page.Should().Be(2);
        capturedCriteria.PageSize.Should().Be(15);
    }

    [Fact]
    public async Task GetManageableEvent_ShouldThrowForbidden_WhenUserCannotManageEvent()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 221, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);

        var action = () => harness.Service.GetManageableEvent(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task UpdateEvent_ShouldRejectCancelledEvents()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(id: 231, lifecycleState: EventLifecycleState.Cancelled);

        var action = () => harness.Service.UpdateEvent(
            ev.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            name: "Updated Event",
            description: "A rewritten description for a cancelled event.",
            location: "Updated Hall",
            imageUrls: null,
            startTime: DateTime.UtcNow.AddDays(5),
            endTime: DateTime.UtcNow.AddDays(5).AddHours(2),
            isPrivate: false,
            maxParticipants: 20,
            registerCost: 0,
            category: EventCategory.Gaming,
            venueName: "Room B",
            city: "Toronto",
            latitude: null,
            longitude: null,
            tags: ["games"]);

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Cannot update a cancelled event.");
    }

    [Fact]
    public async Task UpdateEvent_ShouldRejectMaxParticipantsBelowRegistrationCount()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(id: 241, lifecycleState: EventLifecycleState.Published);
        ev.RegistrationCount = 6;
        await harness.Db.SaveChangesAsync();

        var action = () => harness.Service.UpdateEvent(
            ev.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            name: "Updated Event",
            description: "A rewritten description with a too-small participant cap.",
            location: "Updated Hall",
            imageUrls: null,
            startTime: DateTime.UtcNow.AddDays(5),
            endTime: DateTime.UtcNow.AddDays(5).AddHours(2),
            isPrivate: false,
            maxParticipants: 5,
            registerCost: 0,
            category: EventCategory.Gaming,
            venueName: "Room B",
            city: "Toronto",
            latitude: null,
            longitude: null,
            tags: ["games"]);

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("maxParticipants (5) cannot be less than the current registration count (6).");
    }

    [Fact]
    public async Task GetEventAnalytics_ShouldComputeFillRateAndSpotsRemaining()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 251, clubId: harness.ClubId, name: "Analytics Event");
        ev.maxParticipants = 40;
        ev.LifecycleState = EventLifecycleState.Published;
        harness.SetupCachedEvent(ev);
        harness.AnalyticsRepositoryMock
            .Setup(repository => repository.GetEventAnalyticsAsync(ev.Id))
            .ReturnsAsync(new EventAnalyticsData(
                RegistrationCount: 10,
                RegistrationsToday: 2,
                RegistrationsThisWeek: 5,
                RegistrationsThisMonth: 9,
                TotalRevenue: 5000,
                PendingRevenue: 1500,
                RefundedAmount: 250));

        var response = await harness.Service.GetEventAnalytics(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        response.EventId.Should().Be(ev.Id);
        response.EventName.Should().Be("Analytics Event");
        response.FillRate.Should().Be(25.0);
        response.SpotsRemaining.Should().Be(30);
        response.TotalRevenue.Should().Be(5000);
        response.PendingRevenue.Should().Be(1500);
        response.RefundedAmount.Should().Be(250);
    }

    [Fact]
    public async Task GetClubAnalytics_ShouldBuildRankingsAndAverageFillRate()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.AnalyticsRepositoryMock
            .Setup(repository => repository.GetClubAnalyticsAsync(harness.ClubId))
            .ReturnsAsync(new ClubAnalyticsData(
                TotalEvents: 3,
                DraftEvents: 1,
                PublishedEvents: 2,
                CancelledEvents: 0,
                ArchivedEvents: 0,
                UpcomingEvents: 2,
                OngoingEvents: 0,
                PastEvents: 1,
                TotalRegistrations: 42,
                UniqueAttendees: 30,
                RepeatAttendees: 8,
                TotalRevenue: 11000,
                PendingRevenue: 2200,
                PerEvent:
                [
                    new PerEventAnalytics(1, "Big Night", EventLifecycleState.Published, 100, 50, 7000),
                    new PerEventAnalytics(2, "Small Workshop", EventLifecycleState.Published, 20, 18, 2500),
                    new PerEventAnalytics(3, "Draft Idea", EventLifecycleState.Draft, 10, 0, 0)
                ],
                DailyTrend:
                [
                    (new DateOnly(2026, 6, 1), 4),
                    (new DateOnly(2026, 6, 2), 7)
                ],
                RevenueTrend:
                [
                    (new DateOnly(2026, 6, 1), 1000L),
                    (new DateOnly(2026, 6, 2), 2100L)
                ]));

        var response = await harness.Service.GetClubAnalytics(harness.ClubId, harness.OwnerUserId, harness.OwnerRole);

        response.ClubId.Should().Be(harness.ClubId);
        response.AvgFillRate.Should().Be(46.67);
        response.TopEventsByRegistrations.First().Name.Should().Be("Big Night");
        response.TopEventsByRevenue.First().Name.Should().Be("Big Night");
        response.TopEventsByFillRate.First().Name.Should().Be("Small Workshop");
        response.RegistrationTrend.Should().HaveCount(2);
        response.RevenueTrend.Should().HaveCount(2);
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRestoreSnapshotFields_WhilePreservingLifecycleState()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(id: 261, lifecycleState: EventLifecycleState.Published);
        ev.Name = "Current Name";
        ev.Description = "Current description";
        ev.Location = "Current Hall";
        ev.CurrentVersionNumber = 2;
        await harness.Db.SaveChangesAsync();

        harness.Db.EventVersions.Add(new EventVersion
        {
            EventId = ev.Id,
            VersionNumber = 1,
            ActionType = EventVersionActions.Create,
            SnapshotJson = JsonSerializer.Serialize(new EventVersionSnapshot
            {
                Name = "Original Name",
                Description = "Original description",
                Location = "Original Hall",
                IsPrivate = false,
                MaxParticipants = 50,
                RegisterCost = 0,
                StartTime = ev.StartTime,
                EndTime = ev.EndTime,
                ClubId = harness.ClubId,
                LifecycleState = EventLifecycleState.Draft,
                Category = EventCategory.Gaming,
                VenueName = "Room A",
                City = "Toronto",
                Tags = ["original"]
            }),
            ChangedFieldsJson = "[]",
            ActorUserId = harness.OwnerUserId,
            ActorRole = harness.OwnerRole,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        var rollback = await harness.Service.RollbackToVersionAsync(
            ev.Id,
            1,
            harness.OwnerUserId,
            harness.OwnerRole);

        rollback.RestoredFromVersionNumber.Should().Be(1);
        rollback.NewVersionNumber.Should().Be(3);
        rollback.Event.Name.Should().Be("Original Name");
        rollback.Event.Description.Should().Be("Original description");
        rollback.Event.Location.Should().Be("Original Hall");
        rollback.Event.LifecycleState.Should().Be(EventLifecycleState.Published);
        rollback.Event.CurrentVersionNumber.Should().Be(3);
        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity => entity.Id == ev.Id && entity.CurrentVersionNumber == 3)),
            Times.Once);
    }

    [Fact]
    public async Task CreateEvent_ShouldPersistDraft_AddImages_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        harness.ConfigureImageRepositoryPersistence();

        var imageUrl = "https://cdn.test/events/create-service.png";
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = (int?)null,
                UserId = harness.OwnerUserId,
                PublicUrl = imageUrl,
                ContentType = "image/png"
            }));

        var created = await harness.Service.CreateEvent(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            "Launch Party",
            "A polished launch event for the new semester.",
            "Innovation Hall",
            [imageUrl],
            DateTime.UtcNow.AddDays(5),
            DateTime.UtcNow.AddDays(5).AddHours(3),
            isPrivate: false,
            maxParticipants: 120,
            registerCost: 0,
            EventCategory.Gaming,
            "Main Stage",
            "Toronto",
            43.6532,
            -79.3832,
            ["Launch", " launch ", "Campus"]);

        created.LifecycleState.Should().Be(EventLifecycleState.Draft);
        created.CurrentVersionNumber.Should().Be(1);
        created.Tags.Should().Equal("launch", "campus");
        created.Images.Select(image => image.ImageUrl).Should().Equal(imageUrl);

        var persisted = harness.LoadEvent(created.Id);
        persisted.Should().NotBeNull();
        persisted!.LifecycleState.Should().Be(EventLifecycleState.Draft);
        persisted.Images.Select(image => image.ImageUrl).Should().Equal(imageUrl);

        var version = await harness.Db.EventVersions.SingleAsync(item => item.EventId == created.Id);
        version.ActionType.Should().Be(EventVersionActions.Create);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity => entity.Id == created.Id)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"event:{created.Id}",
            It.IsAny<backend.main.features.events.Events>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task UpdateEvent_ShouldReplaceImages_DeleteRemovedBlob_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        harness.ConfigureImageRepositoryPersistence();

        var existing = await harness.SeedPersistedEventAsync(
            id: 271,
            lifecycleState: EventLifecycleState.Published,
            imageUrls:
            [
                "https://cdn.test/events/remove-me.png",
                "https://cdn.test/events/keep-me.png"
            ]);

        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdAsync(existing.Id))
            .ReturnsAsync(() => harness.LoadEvent(existing.Id));

        var newUrl = "https://cdn.test/events/added-later.png";
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = existing.Id,
                UserId = harness.OwnerUserId,
                PublicUrl = newUrl,
                ContentType = "image/png"
            }));

        var updated = await harness.Service.UpdateEvent(
            existing.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            "Updated Launch Party",
            "A refreshed published event with a stronger schedule.",
            "Downtown Campus",
            ["https://cdn.test/events/keep-me.png", newUrl],
            DateTime.UtcNow.AddDays(10),
            DateTime.UtcNow.AddDays(10).AddHours(4),
            isPrivate: true,
            maxParticipants: 75,
            registerCost: 0,
            EventCategory.Gaming,
            "Atrium",
            "Toronto",
            43.7,
            -79.4,
            ["Featured", " featured ", "Night"]);

        updated.Name.Should().Be("Updated Launch Party");
        updated.isPrivate.Should().BeTrue();
        updated.CurrentVersionNumber.Should().Be(2);
        updated.Tags.Should().Equal("featured", "night");
        updated.Images.Select(image => image.ImageUrl)
            .Should().Equal("https://cdn.test/events/keep-me.png", newUrl);

        var persisted = harness.LoadEvent(existing.Id);
        persisted.Should().NotBeNull();
        persisted!.CurrentVersionNumber.Should().Be(2);
        persisted.Images.Select(image => image.ImageUrl)
            .Should().Equal("https://cdn.test/events/keep-me.png", newUrl);

        var latestVersion = await harness.Db.EventVersions
            .Where(item => item.EventId == existing.Id)
            .OrderByDescending(item => item.VersionNumber)
            .FirstAsync();
        latestVersion.ActionType.Should().Be(EventVersionActions.Update);

        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/remove-me.png"), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity => entity.Id == existing.Id && entity.CurrentVersionNumber == 2)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"event:{existing.Id}",
            It.IsAny<backend.main.features.events.Events>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task ArchiveEvent_ShouldTransitionPublishedEvent_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = await harness.SeedPersistedEventAsync(
            id: 281,
            lifecycleState: EventLifecycleState.Published,
            imageUrls: ["https://cdn.test/events/archive.png"]);

        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdAsync(ev.Id))
            .ReturnsAsync(() => harness.LoadEvent(ev.Id));

        var archived = await harness.Service.ArchiveEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        archived.LifecycleState.Should().Be(EventLifecycleState.Archived);
        archived.CurrentVersionNumber.Should().Be(2);

        var persisted = await harness.Db.Events.SingleAsync(item => item.Id == ev.Id);
        persisted.LifecycleState.Should().Be(EventLifecycleState.Archived);
        persisted.CurrentVersionNumber.Should().Be(2);

        var latestVersion = await harness.Db.EventVersions
            .Where(item => item.EventId == ev.Id)
            .OrderByDescending(item => item.VersionNumber)
            .FirstAsync();
        latestVersion.ActionType.Should().Be(EventVersionActions.Archive);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity =>
                entity.Id == ev.Id &&
                entity.LifecycleState == EventLifecycleState.Archived)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"event:{ev.Id}",
            It.IsAny<backend.main.features.events.Events>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task DeleteEvent_ShouldRemoveEvent_StageDelete_DeleteBlobs_AndRefreshCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();

        var ev = await harness.SeedPersistedEventAsync(
            id: 291,
            lifecycleState: EventLifecycleState.Published,
            imageUrls:
            [
                "https://cdn.test/events/delete-1.png",
                "https://cdn.test/events/delete-2.png"
            ]);
        harness.SetupCachedEvent(ev);

        await harness.Service.DeleteEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        harness.LoadEvent(ev.Id).Should().BeNull();
        harness.OutboxWriterMock.Verify(writer => writer.StageDelete(ev.Id), Times.Once);
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/delete-1.png"), Times.Once);
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync("https://cdn.test/events/delete-2.png"), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{ev.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task BatchCreateEvents_ShouldPersistPublishedEvents_AddImages_AndStageSearchSync()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        harness.ConfigureImageRepositoryPersistence();

        var firstUrl = "https://cdn.test/events/batch-first.png";
        var secondUrl = "https://cdn.test/events/batch-second.png";

        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .SetupSequence(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = (int?)null,
                UserId = harness.OwnerUserId,
                PublicUrl = firstUrl,
                ContentType = "image/png"
            }))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = (int?)null,
                UserId = harness.OwnerUserId,
                PublicUrl = secondUrl,
                ContentType = "image/png"
            }));

        var response = await harness.Service.BatchCreateEvents(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            [
                new backend.main.features.events.contracts.requests.BatchCreateEventItem
                {
                    Name = "Campus Expo",
                    Description = "A campus-wide showcase for clubs and upcoming events.",
                    Location = "Main Field",
                    ImageUrls = [firstUrl],
                    IsPrivate = false,
                    MaxParticipants = 200,
                    RegisterCost = 0,
                    StartTime = DateTime.UtcNow.AddDays(6),
                    EndTime = DateTime.UtcNow.AddDays(6).AddHours(5),
                    Category = EventCategory.Gaming,
                    VenueName = "North Tent",
                    City = "Toronto",
                    Tags = ["Expo", " expo ", "Campus"]
                },
                new backend.main.features.events.contracts.requests.BatchCreateEventItem
                {
                    Name = "Arcade Finals",
                    Description = "The final tournament night for the spring arcade ladder.",
                    Location = "Student Union",
                    ImageUrls = [secondUrl],
                    IsPrivate = false,
                    MaxParticipants = 64,
                    RegisterCost = 0,
                    StartTime = DateTime.UtcNow.AddDays(8),
                    EndTime = DateTime.UtcNow.AddDays(8).AddHours(4),
                    Category = EventCategory.Gaming,
                    VenueName = "South Hall",
                    City = "Toronto",
                    Tags = ["Finals", "Arcade"]
                }
            ]);

        response.Created.Should().HaveCount(2);
        response.Created.Select(item => item.Name).Should().Equal("Campus Expo", "Arcade Finals");

        var persisted = await harness.Db.Events
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().OnlyContain(item => item.LifecycleState == EventLifecycleState.Published);
        persisted[0].Tags.Should().Equal("expo", "campus");
        persisted[1].Tags.Should().Equal("finals", "arcade");

        var versions = await harness.Db.EventVersions
            .OrderBy(item => item.EventId)
            .ToListAsync();
        versions.Should().HaveCount(2);
        versions.Should().OnlyContain(item => item.ActionType == EventVersionActions.Create);

        var images = await harness.Db.EventImages
            .OrderBy(item => item.EventId)
            .ThenBy(item => item.SortOrder)
            .Select(item => item.ImageUrl)
            .ToListAsync();
        images.Should().Equal(firstUrl, secondUrl);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(It.IsAny<backend.main.features.events.Events>()), Times.Exactly(2));
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task GetVisibleEvent_ShouldAllowPrivateEvent_ForClubStaff()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 341, isPrivate: true);
        harness.SetupCachedEvent(ev);
        harness.ClubServiceMock
            .Setup(service => service.HasClubStaffAccessAsync(ev.ClubId, harness.ViewerUserId, harness.ViewerRole))
            .ReturnsAsync(true);

        var visible = await harness.Service.GetVisibleEvent(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        visible.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task GetVisibleEvent_ShouldAllowPrivateEvent_ForAcceptedInvitation()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 342, isPrivate: true);
        harness.SetupCachedEvent(ev);
        harness.InvitationServiceMock
            .Setup(service => service.HasAcceptedInvitationAccessAsync(ev.Id, harness.ViewerUserId))
            .ReturnsAsync(true);

        var visible = await harness.Service.GetVisibleEvent(ev.Id, harness.ViewerUserId, harness.ViewerRole);

        visible.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task GetVisibleEvent_ShouldThrowNotFound_ForArchivedEvent()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 343, isPrivate: false);
        ev.LifecycleState = EventLifecycleState.Archived;
        harness.SetupCachedEvent(ev);

        var action = () => harness.Service.GetVisibleEvent(ev.Id);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Event {ev.Id} not found");
    }

    [Fact]
    public async Task GetManageableEvent_ShouldReturnEvent_ForManager()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 344, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);

        var manageable = await harness.Service.GetManageableEvent(ev.Id, harness.OwnerUserId, harness.OwnerRole);

        manageable.Should().BeSameAs(ev);
    }

    [Fact]
    public async Task GetEventsByIds_ShouldReturnRepositoryResults()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var first = harness.BuildEvent(id: 351, clubId: harness.ClubId, name: "First Item");
        var second = harness.BuildEvent(id: 352, clubId: harness.ClubId, name: "Second Item");
        harness.EventsRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([first, second]);

        var result = await harness.Service.GetEventsByIds([second.Id, first.Id]);

        result.Should().Equal(first, second);
    }

    [Fact]
    public async Task GenerateImageUploadUrlAsync_ShouldUseEventScopedPath_WhenEventIdProvided()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 361, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);

        var uploadResponse = new PresignedUploadResponse
        {
            UploadUrl = "https://upload.test/events/event-scoped",
            PublicUrl = "https://cdn.test/events/event-scoped.png",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        harness.BlobServiceMock
            .Setup(service => service.GenerateUploadUrlAsync(
                $"events/clubs/{harness.ClubId}/events/{ev.Id}",
                "event-scoped.png",
                " image/png "))
            .ReturnsAsync(uploadResponse);

        string? storedPayload = null;
        harness.CacheMock
            .Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((_, payload, _) => storedPayload = payload)
            .ReturnsAsync(true);

        var result = await harness.Service.GenerateImageUploadUrlAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            "event-scoped.png",
            " image/png ",
            ev.Id);

        result.PublicUrl.Should().Be(uploadResponse.PublicUrl);
        storedPayload.Should().NotBeNull();
        storedPayload.Should().Contain($"\"EventId\":{ev.Id}");
    }

    [Fact]
    public async Task AddEventImageAsync_ShouldPersistImage_ClearCache_AndBumpVersion()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureImageRepositoryPersistence();

        var ev = await harness.SeedPersistedEventAsync(
            id: 371,
            lifecycleState: EventLifecycleState.Published,
            imageUrls: ["https://cdn.test/events/existing.png"]);
        harness.SetupCachedEvent(ev);

        var newUrl = "https://cdn.test/events/extra.png";
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                ClubId = harness.ClubId,
                EventId = ev.Id,
                UserId = harness.OwnerUserId,
                PublicUrl = newUrl,
                ContentType = "image/png"
            }));

        var created = await harness.Service.AddEventImageAsync(
            ev.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            newUrl);

        created.ImageUrl.Should().Be(newUrl);
        var images = await harness.Db.EventImages
            .Where(item => item.EventId == ev.Id)
            .OrderBy(item => item.SortOrder)
            .Select(item => item.ImageUrl)
            .ToListAsync();
        images.Should().Equal("https://cdn.test/events/existing.png", newUrl);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{ev.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task BatchUpdateEvents_ShouldApplyPatches_CreateVersions_AndClearCaches()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();

        var first = await harness.SeedPersistedEventAsync(
            id: 381,
            lifecycleState: EventLifecycleState.Published,
            imageUrls: ["https://cdn.test/events/update-a.png"]);
        var second = await harness.SeedPersistedEventAsync(
            id: 382,
            lifecycleState: EventLifecycleState.Published,
            imageUrls: ["https://cdn.test/events/update-b.png"]);

        var updatedCount = await harness.Service.BatchUpdateEvents(
            harness.OwnerUserId,
            harness.OwnerRole,
            [
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem
                {
                    EventId = first.Id,
                    Name = "Renamed First",
                    Tags = ["Night", " night ", "Campus"]
                },
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem
                {
                    EventId = second.Id,
                    Location = "Updated Hall",
                    MaxParticipants = 80,
                    Tags = ["Expo"]
                }
            ]);

        updatedCount.Should().Be(2);

        var refreshedFirst = harness.LoadEvent(first.Id);
        var refreshedSecond = harness.LoadEvent(second.Id);
        refreshedFirst!.Name.Should().Be("Renamed First");
        refreshedFirst.Tags.Should().Equal("night", "campus");
        refreshedFirst.CurrentVersionNumber.Should().Be(2);
        refreshedSecond!.Location.Should().Be("Updated Hall");
        refreshedSecond.maxParticipants.Should().Be(80);
        refreshedSecond.Tags.Should().Equal("expo");
        refreshedSecond.CurrentVersionNumber.Should().Be(2);

        var versions = await harness.Db.EventVersions
            .Where(item => item.EventId == first.Id || item.EventId == second.Id)
            .ToListAsync();
        versions.Should().HaveCount(2);
        versions.Should().OnlyContain(item => item.ActionType == EventVersionActions.Update);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(It.IsAny<backend.main.features.events.Events>()), Times.Exactly(2));
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{first.Id}"), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{second.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("events:version", 1), Times.Once);
    }

    [Fact]
    public async Task BatchUpdateEvents_ShouldRejectDraftEvents()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        var ev = await harness.SeedPersistedEventAsync(
            id: 391,
            lifecycleState: EventLifecycleState.Draft,
            imageUrls: ["https://cdn.test/events/draft-batch.png"]);

        var action = () => harness.Service.BatchUpdateEvents(
            harness.OwnerUserId,
            harness.OwnerRole,
            [
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem
                {
                    EventId = ev.Id,
                    Name = "Illegal Batch Update"
                }
            ]);

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage($"Event {ev.Id} must be updated through the draft workflow.");
    }

    [Fact]
    public async Task BatchUpdateEvents_ShouldRejectWhenPatchedEventIsNotPublishReady()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        harness.ConfigureEventPersistence();
        var ev = await harness.SeedPersistedEventAsync(
            id: 392,
            lifecycleState: EventLifecycleState.Published,
            imageUrls: ["https://cdn.test/events/publish-ready.png"]);

        var action = () => harness.Service.BatchUpdateEvents(
            harness.OwnerUserId,
            harness.OwnerRole,
            [
                new backend.main.features.events.contracts.requests.BatchUpdateEventItem
                {
                    EventId = ev.Id,
                    MaxParticipants = 0
                }
            ]);

        var exception = await action.Should().ThrowAsync<BadRequestException>();
        exception.Which.Message.Should().Contain($"Event {ev.Id} is not publish-ready");
        exception.Which.Message.Should().Contain("Max participants must be between 1 and 10,000.");
    }

    [Fact]
    public async Task GenerateImageUploadUrlAsync_ShouldThrowForbidden_WhenEventBelongsToDifferentClub()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 401, clubId: 77);
        harness.SetupCachedEvent(ev);
        harness.ClubServiceMock
            .Setup(service => service.CanManageEventMediaAsync(77, harness.OwnerUserId, harness.OwnerRole))
            .ReturnsAsync(true);

        var action = () => harness.Service.GenerateImageUploadUrlAsync(
            harness.ClubId,
            harness.OwnerUserId,
            harness.OwnerRole,
            "mismatch.png",
            "image/png",
            ev.Id);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task AddEventImageAsync_ShouldRejectExpiredUploadIntent()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 402, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);
        harness.BlobServiceMock
            .Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
            .Returns(true);
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var action = () => harness.Service.AddEventImageAsync(
            ev.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            "https://cdn.test/events/expired-upload.png");

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Image upload is invalid or expired. Please upload the image again.");
    }

    [Fact]
    public async Task RemoveEventImageAsync_ShouldThrowNotFound_WhenImageDoesNotExist()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();
        var ev = harness.BuildEvent(id: 403, clubId: harness.ClubId);
        harness.SetupCachedEvent(ev);
        harness.ImageRepositoryMock
            .Setup(repository => repository.GetByIdAsync(999, ev.Id))
            .ReturnsAsync((EventImage?)null);

        var action = () => harness.Service.RemoveEventImageAsync(
            ev.Id,
            999,
            harness.OwnerUserId,
            harness.OwnerRole);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Image 999 not found on event {ev.Id}");
    }

    [Fact]
    public async Task GetEventListVersionAsync_ShouldInitializeMissingValue_AndReuseExistingValue()
    {
        await using var harness = await EventsServiceHarness.CreateAsync();

        harness.CacheMock
            .SetupSequence(cache => cache.GetValueAsync("events:version"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("7");

        var first = await InvokePrivateAsync<long>(harness.Service, "GetEventListVersionAsync");
        var second = await InvokePrivateAsync<long>(harness.Service, "GetEventListVersionAsync");

        first.Should().Be(1);
        second.Should().Be(7);
        harness.CacheMock.Verify(cache => cache.SetValueAsync("events:version", "1", null), Times.Once);
    }

    [Fact]
    public void BuildDistanceMap_ShouldIncludeOnlyEventsWithCoordinates_AndCalculateDistance()
    {
        var events = new List<backend.main.features.events.Events>
        {
            new()
            {
                Id = 501,
                Latitude = 43.7000,
                Longitude = -79.4000
            },
            new()
            {
                Id = 502,
                Latitude = null,
                Longitude = null
            }
        };

        var criteria = new EventSearchCriteria
        {
            Lat = 43.6532,
            Lng = -79.3832
        };

        var result = InvokePrivateStatic<Dictionary<int, double>>(
            typeof(EventsService),
            "BuildDistanceMap",
            events,
            criteria);

        result.Should().ContainSingle();
        result.Should().ContainKey(501);
        result[501].Should().BeGreaterThan(0.0);
        result[501].Should().BeLessThan(10.0);
    }

    [Fact]
    public void WithJitter_ShouldStayWithinConfiguredPercentRange()
    {
        var baseTtl = TimeSpan.FromSeconds(10);

        var jittered = InvokePrivateStatic<TimeSpan>(
            typeof(EventsService),
            "WithJitter",
            baseTtl,
            20);

        jittered.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(8));
        jittered.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void ApplyBatchPatch_ShouldUpdateAllSupportedOptionalFields()
    {
        var ev = new backend.main.features.events.Events
        {
            Name = "Original",
            Description = "Original description",
            Location = "Old Location",
            isPrivate = false,
            maxParticipants = 10,
            registerCost = 0,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Category = EventCategory.Gaming,
            VenueName = "Old Venue",
            City = "Old City",
            Latitude = 10,
            Longitude = 20,
            Tags = ["existing"]
        };

        var startTime = DateTime.UtcNow.AddDays(3);
        var endTime = startTime.AddHours(2);
        var patch = new backend.main.features.events.contracts.requests.BatchUpdateEventItem
        {
            Description = "Updated description",
            IsPrivate = true,
            RegisterCost = 2500,
            StartTime = startTime,
            EndTime = endTime,
            Category = EventCategory.Academic,
            VenueName = "New Venue",
            City = "Toronto",
            Latitude = 43.6532,
            Longitude = -79.3832
        };

        InvokePrivateStatic<object?>(typeof(EventsService), "ApplyBatchPatch", ev, patch);

        ev.Description.Should().Be("Updated description");
        ev.isPrivate.Should().BeTrue();
        ev.registerCost.Should().Be(2500);
        ev.StartTime.Should().Be(startTime);
        ev.EndTime.Should().Be(endTime);
        ev.Category.Should().Be(EventCategory.Academic);
        ev.VenueName.Should().Be("New Venue");
        ev.City.Should().Be("Toronto");
        ev.Latitude.Should().Be(43.6532);
        ev.Longitude.Should().Be(-79.3832);
    }

    [Fact]
    public void NormalizePageSize_ShouldClampToSupportedRange()
    {
        InvokePrivateStatic<int>(typeof(EventsService), "NormalizePageSize", 0).Should().Be(20);
        InvokePrivateStatic<int>(typeof(EventsService), "NormalizePageSize", 101).Should().Be(100);
        InvokePrivateStatic<int>(typeof(EventsService), "NormalizePageSize", 25).Should().Be(25);
    }

    [Fact]
    public void NormalizeTags_ShouldReturnEmptyList_WhenTagsAreNull()
    {
        var normalized = InvokePrivateStatic<List<string>>(typeof(EventsService), "NormalizeTags", (object?)null);

        normalized.Should().BeEmpty();
    }

    private static async Task<T> InvokePrivateAsync<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task<T>)method!.Invoke(target, args)!;
        return await task;
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        return (T)method!.Invoke(null, args)!;
    }

    private sealed class EventsServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public EventsService Service { get; }
        public Mock<IEventsRepository> EventsRepositoryMock { get; } = new();
        public Mock<IEventImageRepository> ImageRepositoryMock { get; } = new();
        public Mock<IClubService> ClubServiceMock { get; } = new();
        public Mock<IAzureBlobService> BlobServiceMock { get; } = new();
        public Mock<ICacheService> CacheMock { get; } = new();
        public Mock<IRefreshAheadCache> RefreshCacheMock { get; } = new();
        public Mock<IEventAnalyticsRepository> AnalyticsRepositoryMock { get; } = new();
        public Mock<IEventSearchService> SearchServiceMock { get; } = new();
        public Mock<IEventSearchOutboxWriter> OutboxWriterMock { get; } = new();
        public Mock<IEventRegistrationRepository> RegistrationRepositoryMock { get; } = new();
        public Mock<IEventInvitationService> InvitationServiceMock { get; } = new();

        public int ClubId => 4;
        public int OwnerUserId => 7;
        public int ViewerUserId => 99;
        public string OwnerRole => "Organizer";
        public string ViewerRole => "Participant";

        private EventsServiceHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;

            ClubServiceMock
                .Setup(service => service.HasClubStaffAccessAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync(false);
            ClubServiceMock
                .Setup(service => service.GetClub(ClubId))
                .ReturnsAsync(new Club
                {
                    Id = ClubId,
                    UserId = OwnerUserId,
                    Name = "Board Games Club",
                    Description = "A club for tabletop events.",
                    Clubtype = ClubType.Gaming,
                    ClubImage = "https://cdn.test/clubs/gaming.png"
                });
            ClubServiceMock
                .Setup(service => service.CanManageClubAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync((int clubId, int userId, string? _) => clubId == ClubId && userId == OwnerUserId);
            ClubServiceMock
                .Setup(service => service.CanManageEventMediaAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>()))
                .ReturnsAsync((int clubId, int userId, string? _) => clubId == ClubId && userId == OwnerUserId);

            RegistrationRepositoryMock
                .Setup(repository => repository.IsRegisteredAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((EventRegistration?)null);

            InvitationServiceMock
                .Setup(service => service.HasAcceptedInvitationAccessAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(false);

            RefreshCacheMock
                .Setup(cache => cache.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            RefreshCacheMock
                .Setup(cache => cache.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<backend.main.features.events.Events>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<JsonSerializerOptions?>()))
                .Returns(Task.CompletedTask);

            CacheMock
                .Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1L);

            BlobServiceMock
                .Setup(service => service.DeleteBlobAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            Service = new EventsService(
                db,
                EventsRepositoryMock.Object,
                ImageRepositoryMock.Object,
                ClubServiceMock.Object,
                BlobServiceMock.Object,
                CacheMock.Object,
                RefreshCacheMock.Object,
                AnalyticsRepositoryMock.Object,
                SearchServiceMock.Object,
                OutboxWriterMock.Object,
                RegistrationRepositoryMock.Object,
                InvitationServiceMock.Object,
                Options.Create(new EventVersioningOptions()),
                TimeProvider.System);
        }

        public static async Task<EventsServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new backend.main.features.profile.User
                {
                    Id = 7,
                    Email = "organizer@test.local",
                    Usertype = "Organizer"
                },
                new backend.main.features.profile.User
                {
                    Id = 99,
                    Email = "viewer@test.local",
                    Usertype = "Participant"
                });

            db.Clubs.Add(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Board Games Club",
                Description = "A club for tabletop events.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/gaming.png"
            });

            await db.SaveChangesAsync();

            return new EventsServiceHarness(connection, db);
        }

        public backend.main.features.events.Events BuildEvent(
            int id = 9,
            int clubId = 4,
            string name = "Board Game Night",
            bool isPrivate = false)
        {
            return new backend.main.features.events.Events
            {
                Id = id,
                ClubId = clubId,
                Name = name,
                Description = "A strategy night for campus players.",
                Location = "Student Center",
                StartTime = DateTime.UtcNow.AddDays(2),
                EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
                LifecycleState = EventLifecycleState.Published,
                isPrivate = isPrivate,
                Category = EventCategory.Gaming
            };
        }

        public void SetupCachedEvent(backend.main.features.events.Events ev)
        {
            RefreshCacheMock
                .Setup(cache => cache.GetOrSetAsync<backend.main.features.events.Events>(
                    $"event:{ev.Id}",
                    It.IsAny<Func<Task<backend.main.features.events.Events?>>>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<double>(),
                    It.IsAny<JsonSerializerOptions?>()))
                .ReturnsAsync(ev);
        }

        public void ConfigureEventPersistence()
        {
            var repository = new EventsRepository(Db);

            EventsRepositoryMock
                .Setup(repository => repository.CreateAsync(It.IsAny<backend.main.features.events.Events>()))
                .Returns((backend.main.features.events.Events ev) => repository.CreateAsync(ev));

            EventsRepositoryMock
                .Setup(repository => repository.GetByIdAsync(It.IsAny<int>()))
                .Returns((int eventId) => repository.GetByIdAsync(eventId));

            EventsRepositoryMock
                .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => repository.GetByIdsAsync(ids));

            EventsRepositoryMock
                .Setup(repository => repository.CreateManyAsync(It.IsAny<IEnumerable<backend.main.features.events.Events>>()))
                .Returns((IEnumerable<backend.main.features.events.Events> eventsToCreate) => repository.CreateManyAsync(eventsToCreate));

            EventsRepositoryMock
                .Setup(repository => repository.DeleteAsync(It.IsAny<int>()))
                .Returns((int eventId) => repository.DeleteAsync(eventId));

            EventsRepositoryMock
                .Setup(repository => repository.DeleteManyAsync(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => repository.DeleteManyAsync(ids));
        }

        public void ConfigureImageRepositoryPersistence()
        {
            ImageRepositoryMock
                .Setup(repository => repository.AddImagesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((int eventId, IEnumerable<string> imageUrls) =>
                {
                    var existingCount = Db.EventImages.Count(image => image.EventId == eventId);
                    var created = imageUrls.Select((url, index) => new EventImage
                    {
                        EventId = eventId,
                        ImageUrl = url,
                        SortOrder = existingCount + index
                    }).ToList();

                    Db.EventImages.AddRange(created);
                    return created;
                });

            ImageRepositoryMock
                .Setup(repository => repository.DeleteAllByEventIdAsync(It.IsAny<int>()))
                .Returns((int eventId) =>
                {
                    var existing = Db.EventImages.Where(image => image.EventId == eventId).ToList();
                    Db.EventImages.RemoveRange(existing);
                    return Task.CompletedTask;
                });

            ImageRepositoryMock
                .Setup(repository => repository.CountByEventIdAsync(It.IsAny<int>()))
                .ReturnsAsync((int eventId) => Db.EventImages.Count(image => image.EventId == eventId));

            ImageRepositoryMock
                .Setup(repository => repository.GetByIdAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((int imageId, int eventId) =>
                    Db.EventImages.FirstOrDefault(image => image.Id == imageId && image.EventId == eventId));

            ImageRepositoryMock
                .Setup(repository => repository.DeleteImageAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((int imageId, int eventId) =>
                {
                    var image = Db.EventImages.FirstOrDefault(item => item.Id == imageId && item.EventId == eventId);
                    if (image == null)
                        return false;

                    Db.EventImages.Remove(image);
                    return true;
                });
        }

        public async Task<backend.main.features.events.Events> SeedPersistedEventAsync(
            int id = 18,
            EventLifecycleState lifecycleState = EventLifecycleState.Draft,
            IEnumerable<string>? imageUrls = null)
        {
            var ev = new backend.main.features.events.Events
            {
                Id = id,
                ClubId = ClubId,
                Name = "Publishable Event",
                Description = "A polished event ready for lifecycle tests.",
                Location = "Student Center",
                StartTime = DateTime.UtcNow.AddDays(2),
                EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
                LifecycleState = lifecycleState,
                isPrivate = false,
                maxParticipants = 50,
                registerCost = 0,
                Category = EventCategory.Gaming,
                CurrentVersionNumber = 1,
                VenueName = "Room 201",
                City = "Toronto",
                Tags = ["games"]
            };

            Db.Events.Add(ev);
            await Db.SaveChangesAsync();

            var urls = imageUrls?.ToList() ?? [];
            if (urls.Count > 0)
            {
                for (var index = 0; index < urls.Count; index++)
                {
                    Db.EventImages.Add(new EventImage
                    {
                        EventId = ev.Id,
                        ImageUrl = urls[index],
                        SortOrder = index
                    });
                }

                await Db.SaveChangesAsync();
            }

            return await Db.Events.Include(item => item.Images).SingleAsync(item => item.Id == ev.Id);
        }

        public backend.main.features.events.Events? LoadEvent(int eventId) =>
            Db.Events
                .AsNoTracking()
                .Include(item => item.Images)
                .FirstOrDefault(item => item.Id == eventId);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
