using System.Security.Claims;

using backend.main.features.events;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.series;
using backend.main.features.events.series.contracts.requests;
using backend.main.features.events.series.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events.Series;

public class EventSeriesControllerTests
{
    [Fact]
    public async Task PreviewSeries_ShouldReportHowManyOccurrencesTheRuleProduces()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.PreviewAsync(4, 7, "Organizer", It.IsAny<EventRecurrenceRuleRequest>()))
            .ReturnsAsync(new EventSeriesPreviewResponse { OccurrenceCount = 6 });

        var result = await CreateController(service.Object)
            .PreviewSeries(4, new PreviewEventSeriesRequest());

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<EventSeriesPreviewResponse>>()
            .Which.Message.Should().Contain("6 occurrences");
    }

    [Fact]
    public async Task CreateSeries_ShouldReturn201WithTheGeneratedOccurrences()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.CreateFromDraftAsync(11, 7, "Organizer", It.IsAny<CreateEventSeriesRequest>()))
            .ReturnsAsync(new EventSeriesResponse
            {
                Id = 3,
                Occurrences = [new ManagedEventResponse(), new ManagedEventResponse()]
            });

        var result = await CreateController(service.Object)
            .CreateSeries(11, new CreateEventSeriesRequest());

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeOfType<ApiResponse<EventSeriesResponse>>()
            .Which.Message.Should().Contain("2 occurrences");
    }

    [Fact]
    public async Task GetSeries_ShouldReturnTheSeries()
    {
        var service = new Mock<IEventSeriesService>();
        service.Setup(s => s.GetAsync(3, 7, "Organizer")).ReturnsAsync(new EventSeriesResponse { Id = 3 });

        var result = await CreateController(service.Object).GetSeries(3);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<EventSeriesResponse>>()
            .Which.Data!.Id.Should().Be(3);
    }

    [Fact]
    public async Task GetClubSeries_ShouldReturnAPagedEnvelope()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.GetByClubAsync(4, 7, "Organizer", 2, 10))
            .ReturnsAsync((new List<EventSeriesSummaryResponse> { new() { Id = 3 } }, 1));

        var result = await CreateController(service.Object).GetClubSeries(4, 2, 10);

        var paged = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<PagedResponse<EventSeriesSummaryResponse>>>()
            .Which.Data!;

        paged.Items.Should().ContainSingle();
        paged.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtendSeries_ShouldReportTheNewTotal()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.ExtendAsync(3, 7, "Organizer", It.IsAny<ExtendEventSeriesRequest>()))
            .ReturnsAsync(new EventSeriesResponse
            {
                Id = 3,
                Occurrences = [new ManagedEventResponse(), new ManagedEventResponse(), new ManagedEventResponse()]
            });

        var result = await CreateController(service.Object)
            .ExtendSeries(3, new ExtendEventSeriesRequest { OccurrenceCount = 3 });

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<EventSeriesResponse>>()
            .Which.Message.Should().Contain("3 occurrences");
    }

    [Fact]
    public async Task PublishSeries_ShouldMentionOccurrencesLeftUnchanged()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.PublishAsync(3, 7, "Organizer"))
            .ReturnsAsync(new EventSeriesBulkResultResponse
            {
                SeriesId = 3,
                AffectedCount = 2,
                Skipped = [new EventSeriesSkippedOccurrence { EventId = 9, Reason = "not-publish-ready" }]
            });

        var result = await CreateController(service.Object).PublishSeries(3);

        var message = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<EventSeriesBulkResultResponse>>()
            .Which.Message;

        message.Should().Contain("2 occurrences published");
        message.Should().Contain("1 left unchanged");
    }

    [Fact]
    public async Task UpdateFutureOccurrences_ShouldUseSingularWordingForOneOccurrence()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.UpdateFutureOccurrencesAsync(3, 7, "Organizer", It.IsAny<UpdateFutureOccurrencesRequest>()))
            .ReturnsAsync(new EventSeriesBulkResultResponse { SeriesId = 3, AffectedCount = 1 });

        var result = await CreateController(service.Object)
            .UpdateFutureOccurrences(3, new UpdateFutureOccurrencesRequest { FromEventId = 12 });

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<EventSeriesBulkResultResponse>>()
            .Which.Message.Should().Contain("1 occurrence updated");
    }

    [Fact]
    public async Task CancelSeries_ShouldDefaultToFutureOnlyWhenNoBodyIsSent()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.CancelAsync(3, 7, "Organizer", It.IsAny<CancelEventSeriesRequest>()))
            .ReturnsAsync(new EventSeriesBulkResultResponse { SeriesId = 3, AffectedCount = 2 });

        var result = await CreateController(service.Object).CancelSeries(3);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(
            s => s.CancelAsync(3, 7, "Organizer", It.Is<CancelEventSeriesRequest>(r => r.FutureOnly)),
            Times.Once);
    }

    [Fact]
    public async Task DeleteSeries_ShouldDefaultToTheFutureDraftsScope()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.DeleteAsync(3, 7, "Organizer", It.IsAny<DeleteEventSeriesRequest>()))
            .ReturnsAsync(new EventSeriesBulkResultResponse { SeriesId = 3 });

        var result = await CreateController(service.Object).DeleteSeries(3);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(
            s => s.DeleteAsync(
                3,
                7,
                "Organizer",
                It.Is<DeleteEventSeriesRequest>(r => r.Scope == EventSeriesDeleteScope.FutureDrafts)),
            Times.Once);
    }

    [Fact]
    public async Task DetachOccurrence_ShouldReturnTheEventAsAStandaloneManagedEvent()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.DetachOccurrenceAsync(12, 7, "Organizer"))
            .ReturnsAsync(new EventEntity { Id = 12, ClubId = 4, SeriesId = null });

        var result = await CreateController(service.Object).DetachOccurrence(12);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<ManagedEventResponse>>()
            .Which.Data!.SeriesId.Should().BeNull();
    }

    [Fact]
    public async Task Actions_ShouldSurfaceDomainErrorsThroughTheSharedHandler()
    {
        var service = new Mock<IEventSeriesService>();
        service
            .Setup(s => s.GetAsync(3, 7, "Organizer"))
            .ThrowsAsync(new ResourceNotFoundException("Series 3 not found"));

        var result = await CreateController(service.Object).GetSeries(3);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    private static EventSeriesController CreateController(IEventSeriesService seriesService) =>
        new(seriesService)
        {
            ControllerContext = new ControllerContext
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
            }
        };
}
