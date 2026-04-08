using backend.main.configurations.security;
using backend.main.dtos.requests.events;
using backend.main.dtos.responses.events;
using backend.main.dtos.responses.general;
using backend.main.exceptions.http;
using backend.main.Mappers;
using backend.main.models.enums;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("events")]
    public class EventsController : ControllerBase
    {
        private readonly IEventsService _eventService;

        public EventsController(IEventsService eventService)
        {
            _eventService = eventService;
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPost("{clubId}")]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateRequest request, int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var ev = await _eventService.CreateEvent(
                    clubId,
                    user.Id,
                    request.Name,
                    request.Description,
                    request.Location,
                    request.ImageUrls,
                    request.StartTime,
                    request.EndTime,
                    request.IsPrivate,
                    request.MaxParticipants,
                    request.RegisterCost
                );

                var response = EventMapper.MapToResponse(ev);

                return StatusCode(201,
                    new ApiResponse<EventResponse>(
                        $"The event with ID {ev.Id} has been created successfully.",
                        response
                    ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] CreateEvent failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPut("{eventId}")]
        public async Task<IActionResult> UpdateEvent([FromBody] EventUpdateRequest request, int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var ev = await _eventService.UpdateEvent(
                    eventId,
                    user.Id,
                    request.Name,
                    request.Description,
                    request.Location,
                    request.ImageUrls,
                    request.StartTime,
                    request.EndTime,
                    request.IsPrivate,
                    request.MaxParticipants,
                    request.RegisterCost
                );

                return Ok(new ApiResponse<EventResponse>(
                    $"The event with ID {eventId} has been updated successfully.",
                    EventMapper.MapToResponse(ev)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] UpdateEvent failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpDelete("{eventId}")]
        public async Task<IActionResult> DeleteEvent(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _eventService.DeleteEvent(eventId, user.Id);

                return Ok(new MessageResponse(
                    $"The event with ID {eventId} has been deleted successfully."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] DeleteEvent failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("clubs/{clubId}")]
        public async Task<IActionResult> GetEventsByClub(int clubId, EventStatus? status = null, int page = 1, int pageSize = 20)
        {
            try
            {
                var events = await _eventService.GetEventsByClub(clubId, status: status, page: page, pageSize: pageSize);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    $"The events for club {clubId} have been fetched successfully.",
                    events.Select(e => EventMapper.MapToResponse(e))
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetEventsByClub failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("{eventId}")]
        public async Task<IActionResult> GetEvent(int eventId)
        {
            try
            {
                var ev = await _eventService.GetEvent(eventId);

                return Ok(new ApiResponse<EventResponse>(
                    $"The event with ID {eventId} has been fetched successfully.",
                    EventMapper.MapToResponse(ev)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetEvent failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("")]
        public async Task<IActionResult> GetEvents(
            string? search,
            bool isPrivate = false,
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var events = await _eventService.GetEvents(search, isPrivate, status, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    "The events have been fetched successfully.",
                    events.Select(e => EventMapper.MapToResponse(e))
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetEvents failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("batch")]
        public async Task<IActionResult> GetEventsBatch([FromQuery] string ids)
        {
            try
            {
                var parsedIds = (ids ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) ? (int?)id : null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Take(50)
                    .ToList();

                if (parsedIds.Count == 0)
                    return BadRequest(new MessageResponse("No valid IDs provided."));

                var events = await _eventService.GetEventsByIds(parsedIds);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    $"{events.Count} event(s) fetched successfully.",
                    events.Select(EventMapper.MapToResponse)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetEventsBatch failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPost("batch/{clubId}")]
        public async Task<IActionResult> BatchCreateEvents([FromBody] BatchCreateEventRequest request, int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _eventService.BatchCreateEvents(clubId, user.Id, request.Events);

                return StatusCode(201, new ApiResponse<BatchCreateResultResponse>(
                    $"{result.Created.Count} event(s) created successfully.",
                    result
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] BatchCreateEvents failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPut("batch")]
        public async Task<IActionResult> BatchUpdateEvents([FromBody] BatchUpdateEventRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var count = await _eventService.BatchUpdateEvents(user.Id, request.Events);

                return Ok(new ApiResponse<object>(
                    $"{count} event(s) updated successfully.",
                    new { updatedCount = count }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] BatchUpdateEvents failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpDelete("batch")]
        public async Task<IActionResult> BatchDeleteEvents([FromBody] BatchDeleteRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var count = await _eventService.BatchDeleteEvents(user.Id, request.Ids);

                return Ok(new ApiResponse<object>(
                    $"{count} event(s) deleted successfully.",
                    new { deletedCount = count }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] BatchDeleteEvents failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpGet("{eventId}/analytics")]
        public async Task<IActionResult> GetEventAnalytics(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var analytics = await _eventService.GetEventAnalytics(eventId, user.Id);

                return Ok(new ApiResponse<EventAnalyticsResponse>(
                    $"Analytics for event {eventId} fetched successfully.",
                    analytics
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetEventAnalytics failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPost("images/presigned-url")]
        public async Task<IActionResult> GetPresignedUploadUrl([FromBody] PresignedUrlRequest request)
        {
            try
            {
                var result = await _eventService.GenerateImageUploadUrlAsync(
                    request.FileName, request.ContentType);

                return Ok(new ApiResponse<PresignedUploadResponse>(
                    "Presigned upload URL generated successfully.",
                    result
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetPresignedUploadUrl failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPost("{eventId}/images")]
        public async Task<IActionResult> AddEventImage([FromBody] AddEventImageRequest request, int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var image = await _eventService.AddEventImageAsync(eventId, user.Id, request.ImageUrl);

                return StatusCode(201, new ApiResponse<object>(
                    $"Image added to event {eventId} successfully.",
                    new { image.Id, image.ImageUrl, image.SortOrder }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] AddEventImage failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpDelete("{eventId}/images/{imageId}")]
        public async Task<IActionResult> RemoveEventImage(int eventId, int imageId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _eventService.RemoveEventImageAsync(eventId, imageId, user.Id);

                return Ok(new MessageResponse(
                    $"Image {imageId} removed from event {eventId} successfully."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] RemoveEventImage failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpGet("clubs/{clubId}/analytics")]
        public async Task<IActionResult> GetClubAnalytics(int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var analytics = await _eventService.GetClubAnalytics(clubId, user.Id);

                return Ok(new ApiResponse<ClubAnalyticsResponse>(
                    $"Analytics for club {clubId} fetched successfully.",
                    analytics
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] GetClubAnalytics failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
