using backend.main.consumers;
using backend.main.features.clubs.posts.search;
using backend.main.features.events;
using backend.main.features.events.search;

using FluentAssertions;

namespace backend.tests.Unit.Consumers;

public class ElasticsearchIndexMessageValidatorTests
{
    [Fact]
    public void ToEventDocument_ShouldMapAndNormalizeValidEvent()
    {
        var createdAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddHours(3);
        var startTime = createdAt.AddDays(2);

        var document = ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = "UPSERT",
            EventId = 14,
            ClubId = 3,
            Name = "  Summer Meetup  ",
            Description = "  Bring snacks.  ",
            Location = "  Main Hall ",
            IsPrivate = true,
            StartTime = startTime,
            EndTime = startTime.AddHours(2),
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Category = EventCategory.Social,
            VenueName = "  Community Center  ",
            City = "  Toronto ",
            Latitude = 43.6532,
            Longitude = -79.3832,
            Tags =
            [
                " Social ",
                "social",
                "Community",
                "  ",
                "Games"
            ],
            RegistrationCount = 7
        });

        document.Id.Should().Be(14);
        document.ClubId.Should().Be(3);
        document.Name.Should().Be("Summer Meetup");
        document.Description.Should().Be("Bring snacks.");
        document.Location.Should().Be("Main Hall");
        document.IsPrivate.Should().BeTrue();
        document.StartTime.Should().Be(startTime);
        document.EndTime.Should().Be(startTime.AddHours(2));
        document.CreatedAt.Should().Be(createdAt);
        document.UpdatedAt.Should().Be(updatedAt);
        document.Category.Should().Be(EventCategory.Social.ToString());
        document.VenueName.Should().Be("Community Center");
        document.City.Should().Be("Toronto");
        document.Tags.Should().Equal("social", "community", "games");
        document.RegistrationCount.Should().Be(7);
        document.LocationGeo.Should().NotBeNull();
    }

    [Fact]
    public void ToEventDocument_ShouldDefaultOptionalValues_WhenMissing()
    {
        var createdAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var document = ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = "upsert",
            EventId = 10,
            ClubId = 4,
            Name = "Quiet Event",
            Location = "Room 2",
            StartTime = createdAt.AddDays(1),
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(15),
            Category = EventCategory.Academic
        });

        document.Description.Should().BeEmpty();
        document.IsPrivate.Should().BeFalse();
        document.VenueName.Should().BeNull();
        document.City.Should().BeNull();
        document.Tags.Should().BeEmpty();
        document.RegistrationCount.Should().Be(0);
        document.LocationGeo.Should().BeNull();
    }

    [Theory]
    [InlineData("delete", "Delete event message 11 cannot be mapped to an Elasticsearch document.")]
    [InlineData("patch", "event operation 'patch' is not supported.")]
    [InlineData("", "event operation is required.")]
    public void ToEventDocument_ShouldRejectInvalidOperations(string operation, string message)
    {
        var action = () => ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = operation,
            EventId = 11
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage(message);
    }

    [Theory]
    [InlineData(-1, null, "registrationCount cannot be negative.")]
    [InlineData(1, -1, "endTime cannot be earlier than startTime.")]
    public void ToEventDocument_ShouldRejectInvalidEventShape(int registrationCount, int? endOffsetHours, string message)
    {
        var startTime = new DateTime(2026, 6, 3, 18, 0, 0, DateTimeKind.Utc);

        var action = () => ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = "upsert",
            EventId = 5,
            ClubId = 2,
            Name = "Board Game Night",
            Location = "Clubhouse",
            StartTime = startTime,
            EndTime = endOffsetHours.HasValue ? startTime.AddHours(endOffsetHours.Value) : null,
            CreatedAt = startTime.AddDays(-2),
            UpdatedAt = startTime.AddDays(-1),
            Category = EventCategory.Gaming,
            RegistrationCount = registrationCount
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage(message);
    }

    [Fact]
    public void ToEventDocument_ShouldRejectUpdatedBeforeCreated()
    {
        var action = () => ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = "upsert",
            EventId = 8,
            ClubId = 2,
            Name = "Hack Night",
            Location = "Lab",
            StartTime = new DateTime(2026, 6, 5, 17, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            Category = EventCategory.Workshop
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage("updatedAt cannot be earlier than createdAt.");
    }

    [Theory]
    [InlineData(40.0, null, "latitude and longitude must both be provided together.")]
    [InlineData(95.0, 70.0, "latitude must be between -90 and 90.")]
    [InlineData(40.0, 190.0, "longitude must be between -180 and 180.")]
    public void ToEventDocument_ShouldRejectInvalidGeoCoordinates(double? latitude, double? longitude, string message)
    {
        var action = () => ElasticsearchIndexMessageValidator.ToEventDocument(new EventIndexEvent
        {
            Operation = "upsert",
            EventId = 16,
            ClubId = 5,
            Name = "Outdoor Run",
            Location = "Park",
            StartTime = new DateTime(2026, 6, 8, 8, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            Category = EventCategory.Sports,
            Latitude = latitude,
            Longitude = longitude
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage(message);
    }

    [Fact]
    public void ToClubPostDocument_ShouldMapAndNormalizeValidPost()
    {
        var createdAt = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        var document = ElasticsearchIndexMessageValidator.ToClubPostDocument(new ClubPostIndexEvent
        {
            Operation = "upsert",
            PostId = 18,
            ClubId = 4,
            UserId = 9,
            Title = "  Tournament Update  ",
            Content = "  Brackets are live. ",
            PostType = " announcement ",
            LikesCount = 12,
            IsPinned = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(20)
        });

        document.Id.Should().Be(18);
        document.ClubId.Should().Be(4);
        document.UserId.Should().Be(9);
        document.Title.Should().Be("Tournament Update");
        document.Content.Should().Be("Brackets are live.");
        document.PostType.Should().Be("announcement");
        document.LikesCount.Should().Be(12);
        document.IsPinned.Should().BeTrue();
    }

    [Fact]
    public void ToClubPostDocument_ShouldDefaultOptionalContentAndFlags()
    {
        var createdAt = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        var document = ElasticsearchIndexMessageValidator.ToClubPostDocument(new ClubPostIndexEvent
        {
            Operation = "upsert",
            PostId = 19,
            ClubId = 4,
            UserId = 9,
            Title = "Weekly Check-In",
            PostType = "discussion",
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(10)
        });

        document.Content.Should().BeEmpty();
        document.IsPinned.Should().BeFalse();
        document.LikesCount.Should().Be(0);
    }

    [Theory]
    [InlineData("delete", "Delete club post message 23 cannot be mapped to an Elasticsearch document.")]
    [InlineData("merge", "club post operation 'merge' is not supported.")]
    public void ToClubPostDocument_ShouldRejectInvalidOperations(string operation, string message)
    {
        var action = () => ElasticsearchIndexMessageValidator.ToClubPostDocument(new ClubPostIndexEvent
        {
            Operation = operation,
            PostId = 23
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage(message);
    }

    [Theory]
    [InlineData(-1, "likesCount cannot be negative.")]
    [InlineData(0, "updatedAt cannot be earlier than createdAt.")]
    public void ToClubPostDocument_ShouldRejectInvalidPostShape(int likesCount, string message)
    {
        var createdAt = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        var action = () => ElasticsearchIndexMessageValidator.ToClubPostDocument(new ClubPostIndexEvent
        {
            Operation = "upsert",
            PostId = 24,
            ClubId = 6,
            UserId = 3,
            Title = "Status Update",
            PostType = "general",
            LikesCount = likesCount,
            CreatedAt = createdAt,
            UpdatedAt = likesCount == 0 ? createdAt.AddMinutes(-1) : createdAt.AddMinutes(1)
        });

        action.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage(message);
    }

    [Fact]
    public void ValidateDelete_ShouldAcceptDeleteOperations_AndRejectUpserts()
    {
        var deleteEvent = new EventIndexEvent
        {
            Operation = "delete",
            EventId = 7
        };
        var deletePost = new ClubPostIndexEvent
        {
            Operation = "delete",
            PostId = 15
        };

        var nonDeleteEventAction = () => ElasticsearchIndexMessageValidator.ValidateDelete(new EventIndexEvent
        {
            Operation = "upsert",
            EventId = 7
        });
        var nonDeletePostAction = () => ElasticsearchIndexMessageValidator.ValidateDelete(new ClubPostIndexEvent
        {
            Operation = "upsert",
            PostId = 15
        });

        var eventAction = () => ElasticsearchIndexMessageValidator.ValidateDelete(deleteEvent);
        var postAction = () => ElasticsearchIndexMessageValidator.ValidateDelete(deletePost);

        eventAction.Should().NotThrow();
        postAction.Should().NotThrow();
        nonDeleteEventAction.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage("Event message 7 is not a delete operation.");
        nonDeletePostAction.Should()
            .Throw<ElasticsearchIndexMessageValidationException>()
            .WithMessage("Club post message 15 is not a delete operation.");
    }
}
