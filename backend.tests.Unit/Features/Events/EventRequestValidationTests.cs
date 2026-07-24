using System.ComponentModel.DataAnnotations;

using backend.main.features.events;
using backend.main.features.events.contracts.requests;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events;

public class EventRequestValidationTests
{
    [Fact]
    public void EventCreateRequest_ShouldValidateSuccessfully_ForWellFormedPayload()
    {
        var request = BuildValidEventCreateRequest();

        Validate(request).Should().BeEmpty();
    }

    [Fact]
    public void EventCreateRequest_ShouldReportCustomValidationFailures()
    {
        var request = BuildValidEventCreateRequest();
        request.StartTime = DateTime.UtcNow.AddMinutes(-5);
        request.EndTime = request.StartTime.AddMinutes(-1);
        request.IsPrivate = true;
        request.RegisterCost = 10;
        request.ImageUrls = [];
        request.Latitude = 43.0;
        request.Longitude = null;
        request.Tags = Enumerable.Range(0, 11).Select(index => $"tag-{index}").ToList();

        var results = Validate(request);

        results.Select(item => item.ErrorMessage).Should().Contain([
            "StartTime must be in the future.",
            "EndTime must be later than StartTime.",
            "Private events cannot require a registration fee.",
            "At least one image is required.",
            "Latitude and Longitude must both be provided, or both omitted.",
            "A maximum of 10 tags are allowed."
        ]);
    }

    [Fact]
    public void EventCreateRequest_ShouldRejectInvalidHttpsUrlsAndTags()
    {
        var request = BuildValidEventCreateRequest();
        request.ImageUrls = ["http://example.com/image.png", "not-a-url"];
        request.Tags = ["ok-tag", "bad tag"];

        var results = Validate(request);

        results.Select(item => item.ErrorMessage).Should().Contain([
            "'http://example.com/image.png' is not a valid HTTPS URL.",
            "'not-a-url' is not a valid HTTPS URL.",
            "Tag 'bad tag' is invalid. Tags must be 1-30 chars, alphanumeric or dashes."
        ]);
    }

    [Fact]
    public void BatchCreateEventItem_ShouldApplyAttributeRules()
    {
        var item = new BatchCreateEventItem
        {
            Name = "AB",
            Description = "short",
            Location = new string('L', 51),
            MaxParticipants = 0,
            RegisterCost = 60_000,
            VenueName = new string('V', 101),
            City = new string('C', 101),
            Latitude = 95,
            Longitude = -181,
            Category = EventCategory.Gaming
        };

        var results = Validate(item);

        results.Select(item => item.ErrorMessage).Should().Contain([
            "The field Name must be a string with a minimum length of 3 and a maximum length of 30.",
            "The field Description must be a string with a minimum length of 10 and a maximum length of 200.",
            "The field Location must be a string with a maximum length of 50.",
            "Max participants must be between 1 and 10,000.",
            "Register cost must be between $0 and $50,000.",
            "The field VenueName must be a string with a maximum length of 100.",
            "The field City must be a string with a maximum length of 100.",
            "The field Latitude must be between -90 and 90.",
            "The field Longitude must be between -180 and 180."
        ]);
    }

    [Fact]
    public void BatchCreateEventItem_ShouldApplyCustomValidationRules()
    {
        var item = new BatchCreateEventItem
        {
            Name = "Event Name",
            Description = "A long enough description for custom validation.",
            Location = "Venue",
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(-2),
            IsPrivate = true,
            RegisterCost = 25,
            Category = EventCategory.Gaming,
            ImageUrls = ["ftp://bad.test/file.png"],
            Latitude = 43.0,
            Longitude = null,
            Tags = ["valid-tag", "bad tag"]
        };

        var results = item.Validate(new ValidationContext(item)).ToList();

        results.Select(item => item.ErrorMessage).Should().Contain([
            "StartTime must be in the future.",
            "EndTime must be later than StartTime.",
            "Private events cannot require a registration fee.",
            "'ftp://bad.test/file.png' is not a valid HTTPS URL.",
            "Latitude and Longitude must both be provided, or both omitted.",
            "Tag 'bad tag' is invalid. Tags must be 1-30 chars, alphanumeric or dashes."
        ]);
    }

    [Fact]
    public void BatchCreateEventRequest_ShouldRequireBatchSizeBetweenOneAndFifty()
    {
        var empty = new BatchCreateEventRequest
        {
            Events = []
        };
        var tooMany = new BatchCreateEventRequest
        {
            Events = Enumerable.Range(0, 51)
                .Select(_ => new BatchCreateEventItem
                {
                    Name = "Event Name",
                    Description = "Long enough event description",
                    Location = "Venue",
                    StartTime = DateTime.UtcNow.AddDays(1),
                    Category = EventCategory.Gaming,
                    ImageUrls = ["https://ok.test/image.png"]
                })
                .ToList()
        };

        Validate(empty).Select(item => item.ErrorMessage).Should().Contain("Batch size must be between 1 and 50 events.");
        Validate(tooMany).Select(item => item.ErrorMessage).Should().Contain("Batch size must be between 1 and 50 events.");
    }

    [Fact]
    public void PresignedUrlRequest_ShouldValidateRangesAndStringLengths()
    {
        var request = new PresignedUrlRequest
        {
            ClubId = -1,
            EventId = 0,
            FileName = "ab",
            ContentType = new string('x', 101)
        };

        var results = Validate(request);

        results.Select(item => item.ErrorMessage).Should().Contain([
            "ClubId cannot be negative.",
            "EventId must be greater than 0.",
            "The field FileName must be a string with a minimum length of 3 and a maximum length of 255.",
            "The field ContentType must be a string with a maximum length of 100."
        ]);
    }

    [Fact]
    public void AddEventImageRequest_ShouldRequireUrlWithMaxLength()
    {
        var request = new AddEventImageRequest
        {
            ImageUrl = ""
        };

        var emptyResults = Validate(request);
        emptyResults.Select(item => item.ErrorMessage).Should().Contain("The ImageUrl field is required.");

        request.ImageUrl = new string('x', 2049);
        var longResults = Validate(request);
        longResults.Select(item => item.ErrorMessage).Should().Contain("The field ImageUrl must be a string with a maximum length of 2048.");
    }

    private static EventCreateRequest BuildValidEventCreateRequest()
    {
        var startTime = DateTime.UtcNow.AddDays(2);
        return new EventCreateRequest
        {
            Name = "Board Game Night",
            Description = "A long enough description for validation.",
            Location = "Student Center",
            ImageUrls = ["https://cdn.test/events/one.png"],
            MaxParticipants = 20,
            RegisterCost = 0,
            StartTime = startTime,
            EndTime = startTime.AddHours(2),
            Category = EventCategory.Gaming,
            VenueName = "Main Hall",
            City = "Toronto",
            Latitude = 43.65,
            Longitude = -79.38,
            Tags = ["board-games", "casual"]
        };
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
