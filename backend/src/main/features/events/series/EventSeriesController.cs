using System.ComponentModel.DataAnnotations;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.series.contracts.requests;
using backend.main.features.events.series.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.events.series;

/// <summary>
/// Recurrence series endpoints.
/// <para>
/// Only genuinely series-shaped operations live here. Editing, cancelling or deleting a single
/// occurrence already works through the ordinary event endpoints, because an occurrence is an
/// ordinary event — that is the whole design, and duplicating those routes would only create two
/// ways to do the same thing.
/// </para>
/// </summary>
[ApiController]
[FeatureGate(FeatureFlagKeys.EventsRecurrence)]
[Route("events")]
public class EventSeriesController : ControllerBase
{
    private readonly IEventSeriesService _seriesService;

    public EventSeriesController(IEventSeriesService seriesService)
    {
        _seriesService = seriesService;
    }

    /// <summary>Expands a repeat rule without saving anything, for the wizard's live preview.</summary>
    [Authorize]
    [HttpPost("clubs/{clubId}/series/preview")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesPreviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewSeries(
        [Range(1, int.MaxValue)] int clubId,
        [FromBody] PreviewEventSeriesRequest request)
    {
        try
        {
            var user = User.GetUserPayload();

            var preview = await _seriesService.PreviewAsync(clubId, user.Id, user.Role, request.Recurrence);

            return Ok(new ApiResponse<EventSeriesPreviewResponse>(
                $"Generated {preview.OccurrenceCount} occurrences.",
                preview));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(PreviewSeries));
        }
    }

    /// <summary>Turns an existing draft into occurrence 0 of a new series.</summary>
    [Authorize]
    [HttpPost("{eventId}/series")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSeries(
        [Range(1, int.MaxValue)] int eventId,
        [FromBody] CreateEventSeriesRequest request)
    {
        try
        {
            var user = User.GetUserPayload();

            var series = await _seriesService.CreateFromDraftAsync(eventId, user.Id, user.Role, request);

            return StatusCode(201, new ApiResponse<EventSeriesResponse>(
                $"Created a series with {series.Occurrences.Count} occurrences.",
                series));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(CreateSeries));
        }
    }

    [Authorize]
    [HttpGet("series/{seriesId}")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeries([Range(1, int.MaxValue)] int seriesId)
    {
        try
        {
            var user = User.GetUserPayload();

            var series = await _seriesService.GetAsync(seriesId, user.Id, user.Role);

            return Ok(new ApiResponse<EventSeriesResponse>("Series retrieved successfully.", series));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(GetSeries));
        }
    }

    [Authorize]
    [HttpGet("clubs/{clubId}/series")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<EventSeriesSummaryResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClubSeries(
        [Range(1, int.MaxValue)] int clubId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var user = User.GetUserPayload();

            var (series, totalCount) = await _seriesService.GetByClubAsync(
                clubId,
                user.Id,
                user.Role,
                page,
                pageSize);

            return Ok(new ApiResponse<PagedResponse<EventSeriesSummaryResponse>>(
                "Series retrieved successfully.",
                new PagedResponse<EventSeriesSummaryResponse>(series.ToList(), totalCount, page, pageSize)));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(GetClubSeries));
        }
    }

    /// <summary>Generates the extra occurrences a revised terminator adds.</summary>
    [Authorize]
    [HttpPost("series/{seriesId}/extend")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtendSeries(
        [Range(1, int.MaxValue)] int seriesId,
        [FromBody] ExtendEventSeriesRequest request)
    {
        try
        {
            var user = User.GetUserPayload();

            var series = await _seriesService.ExtendAsync(seriesId, user.Id, user.Role, request);

            return Ok(new ApiResponse<EventSeriesResponse>(
                $"The series now has {series.Occurrences.Count} occurrences.",
                series));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(ExtendSeries));
        }
    }

    /// <summary>
    /// Publishes every draft occurrence that passes its checks. Occurrences that do not are
    /// reported in <c>Skipped</c> rather than failing the whole call.
    /// </summary>
    [Authorize]
    [HttpPost("series/{seriesId}/publish")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesBulkResultResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishSeries([Range(1, int.MaxValue)] int seriesId)
    {
        try
        {
            var user = User.GetUserPayload();

            var result = await _seriesService.PublishAsync(seriesId, user.Id, user.Role);

            return Ok(new ApiResponse<EventSeriesBulkResultResponse>(
                DescribeBulkResult(result, "published"),
                result));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(PublishSeries));
        }
    }

    /// <summary>Applies a patch to this occurrence and every later one that has not yet started.</summary>
    [Authorize]
    [HttpPatch("series/{seriesId}/occurrences")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesBulkResultResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFutureOccurrences(
        [Range(1, int.MaxValue)] int seriesId,
        [FromBody] UpdateFutureOccurrencesRequest request)
    {
        try
        {
            var user = User.GetUserPayload();

            var result = await _seriesService.UpdateFutureOccurrencesAsync(
                seriesId,
                user.Id,
                user.Role,
                request);

            return Ok(new ApiResponse<EventSeriesBulkResultResponse>(
                DescribeBulkResult(result, "updated"),
                result));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(UpdateFutureOccurrences));
        }
    }

    /// <summary>Cancels occurrences. Nothing is deleted, and the series row survives.</summary>
    [Authorize]
    [HttpPost("series/{seriesId}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesBulkResultResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelSeries(
        [Range(1, int.MaxValue)] int seriesId,
        [FromBody] CancelEventSeriesRequest? request = null)
    {
        try
        {
            var user = User.GetUserPayload();

            var result = await _seriesService.CancelAsync(
                seriesId,
                user.Id,
                user.Role,
                request ?? new CancelEventSeriesRequest());

            return Ok(new ApiResponse<EventSeriesBulkResultResponse>(
                DescribeBulkResult(result, "cancelled"),
                result));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(CancelSeries));
        }
    }

    /// <summary>
    /// Deletes the series. Occurrences with registrations are always detached rather than
    /// removed, whatever scope is requested.
    /// </summary>
    [Authorize]
    [HttpDelete("series/{seriesId}")]
    [ProducesResponseType(typeof(ApiResponse<EventSeriesBulkResultResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSeries(
        [Range(1, int.MaxValue)] int seriesId,
        [FromBody] DeleteEventSeriesRequest? request = null)
    {
        try
        {
            var user = User.GetUserPayload();

            var result = await _seriesService.DeleteAsync(
                seriesId,
                user.Id,
                user.Role,
                request ?? new DeleteEventSeriesRequest());

            return Ok(new ApiResponse<EventSeriesBulkResultResponse>(
                DescribeBulkResult(result, "deleted"),
                result));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(DeleteSeries));
        }
    }

    /// <summary>Detaches one occurrence, leaving it as an ordinary standalone event.</summary>
    [Authorize]
    [HttpPost("{eventId}/series/detach")]
    [ProducesResponseType(typeof(ApiResponse<ManagedEventResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DetachOccurrence([Range(1, int.MaxValue)] int eventId)
    {
        try
        {
            var user = User.GetUserPayload();

            var detached = await _seriesService.DetachOccurrenceAsync(eventId, user.Id, user.Role);

            return Ok(new ApiResponse<ManagedEventResponse>(
                "This event is no longer part of a series.",
                EventMapper.MapToManagedResponse(
                    detached,
                    EventLifecyclePolicy.GetPublishIssues(detached, DateTime.UtcNow))));
        }
        catch (Exception e)
        {
            return Handle(e, nameof(DetachOccurrence));
        }
    }

    private static string DescribeBulkResult(EventSeriesBulkResultResponse result, string verb)
    {
        var message = $"{result.AffectedCount} {(result.AffectedCount == 1 ? "occurrence" : "occurrences")} {verb}.";

        return result.Skipped.Count == 0
            ? message
            : $"{message} {result.Skipped.Count} left unchanged.";
    }

    private IActionResult Handle(Exception e, string operation)
    {
        if (e is not AppException)
            Logger.Error($"[EventSeriesController] {operation} failed: {e}");

        return HandleError.Resolve(e);
    }
}
