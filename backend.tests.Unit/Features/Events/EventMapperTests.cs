using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;

using FluentAssertions;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events;

public class EventMapperTests
{
    [Fact]
    public void MapToResponse_ShouldMapDefaults_Status_AndOrderedImages()
    {
        var createdAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var ev = new EventEntity
        {
            Id = 7,
            Name = null,
            Description = null,
            Location = null,
            ClubId = 3,
            isPrivate = true,
            maxParticipants = 20,
            registerCost = 5,
            StartTime = null,
            EndTime = null,
            CreatedAt = createdAt,
            LifecycleState = EventLifecycleState.Draft,
            Category = EventCategory.Other,
            Tags = null!,
            RegistrationCount = 9,
            Images =
            [
                new EventImage { ImageUrl = "https://cdn.test/2.png", SortOrder = 2 },
                new EventImage { ImageUrl = "https://cdn.test/1.png", SortOrder = 1 }
            ]
        };

        var response = EventMapper.MapToResponse(ev, 4.2);

        response.Name.Should().BeEmpty();
        response.Description.Should().BeEmpty();
        response.Location.Should().BeEmpty();
        response.ImageUrls.Should().Equal("https://cdn.test/1.png", "https://cdn.test/2.png");
        response.StartTime.Should().Be(createdAt);
        response.Status.Should().Be(EventStatus.Upcoming);
        response.Tags.Should().BeEmpty();
        response.DistanceKm.Should().Be(4.2);
    }

    [Fact]
    public void MapClubToResponse_ShouldMapAllPublicFields()
    {
        var club = new Club
        {
            Id = 8,
            Name = "Chess Club",
            Description = "Weekly chess nights.",
            Clubtype = ClubType.Social,
            ClubImage = "https://cdn.test/club.png",
            MemberCount = 25,
            EventCount = 6,
            AvaliableEventCount = 4,
            isPrivate = true,
            Email = "club@example.com",
            Phone = "555-0100",
            Rating = 4.8,
            WebsiteUrl = "https://club.example.com",
            Location = "Student Center",
            UserId = 3
        };

        var response = EventMapper.MapClubToResponse(club);

        response.Should().BeEquivalentTo(new EventHostClubResponse
        {
            Id = 8,
            Name = "Chess Club",
            Description = "Weekly chess nights.",
            ClubType = ClubType.Social.ToString(),
            ClubImage = "https://cdn.test/club.png",
            MemberCount = 25,
            EventCount = 6,
            AvailableEventCount = 4,
            IsPrivate = true,
            Email = "club@example.com",
            Phone = "555-0100",
            Rating = 4.8,
            WebsiteUrl = "https://club.example.com",
            Location = "Student Center"
        });
    }

    [Fact]
    public void MapToManagedResponse_ShouldMapPublishReadiness_AndNullMaxParticipants()
    {
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var updatedAt = createdAt.AddHours(2);
        var ev = new EventEntity
        {
            Id = 11,
            Name = "Board Game Night",
            Description = "Games and snacks.",
            Location = "Commons",
            ClubId = 5,
            isPrivate = false,
            maxParticipants = 0,
            registerCost = 0,
            StartTime = DateTime.UtcNow.AddDays(3),
            EndTime = DateTime.UtcNow.AddDays(3).AddHours(2),
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            LifecycleState = EventLifecycleState.Published,
            Category = EventCategory.Social,
            VenueName = "Room 101",
            City = "Ottawa",
            Latitude = 45.4,
            Longitude = -75.7,
            Tags = ["social"],
            RegistrationCount = 12,
            Images = [new EventImage { ImageUrl = "https://cdn.test/event.png", SortOrder = 0 }]
        };

        var response = EventMapper.MapToManagedResponse(ev, ["Missing flyer"]);

        response.MaxParticipants.Should().BeNull();
        response.PublishReady.Should().BeFalse();
        response.PublishIssues.Should().ContainSingle("Missing flyer");
        response.Status.Should().Be(EventStatus.Upcoming);
        response.ImageUrls.Should().ContainSingle("https://cdn.test/event.png");
    }

    [Fact]
    public void ResolveOptionalStatus_ShouldReturnNull_ForDraftWithoutStartTime()
    {
        var ev = new EventEntity
        {
            LifecycleState = EventLifecycleState.Draft
        };

        EventMapper.ResolveOptionalStatus(ev).Should().BeNull();
    }
}
