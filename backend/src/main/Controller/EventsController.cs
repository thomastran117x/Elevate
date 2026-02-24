using backend.main.DTOs;
using backend.main.Exceptions;
using backend.main.Interfaces;
using backend.main.Mappers;
using backend.main.Middlewares;
using backend.main.Utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.Controllers
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
        public async Task<IActionResult> CreateEvent([FromForm] EventCreateRequest request, int clubId)
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
                    request.EventImage,
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
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] CreateEvent failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpPut("{eventId}")]
        public async Task<IActionResult> UpdateEvent([FromForm] EventUpdateRequest request, int eventId)
        {
            try
            {
                var user = User.GetUserPayload();
                ValidateUtility.ValidatePositiveId(eventId);

                var ev = await _eventService.UpdateEvent(
                    eventId,
                    user.Id,
                    request.Name,
                    request.Description,
                    request.Location,
                    request.EventImage,
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
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] UpdateEvent failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }

        [Authorize(Policy = "OrganizerOnly")]
        [HttpDelete("{eventId}")]
        public async Task<IActionResult> DeleteEvent(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();
                ValidateUtility.ValidatePositiveId(eventId);

                await _eventService.DeleteEvent(eventId, user.Id);

                return Ok(new MessageResponse(
                    $"The event with ID {eventId} has been deleted successfully."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] DeleteEvent failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }

        [HttpGet("clubs/{clubId}")]
        public async Task<IActionResult> GetEventsByClub(int clubId, int page = 1, int pageSize = 20)
        {
            try
            {
                ValidateUtility.ValidatePositiveId(clubId);

                var events = await _eventService.GetEventsByClub(clubId, page: page, pageSize: pageSize);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    $"The events for club {clubId} have been fetched successfully.",
                    events.Select(EventMapper.MapToResponse)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] GetEventsByClub failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }

        [HttpGet("{eventId}")]
        public async Task<IActionResult> GetEvent(int eventId)
        {
            try
            {
                ValidateUtility.ValidatePositiveId(eventId);

                var ev = await _eventService.GetEvent(eventId);

                return Ok(new ApiResponse<EventResponse>(
                    $"The event with ID {eventId} has been fetched successfully.",
                    EventMapper.MapToResponse(ev)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] GetEvent failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }

        [HttpGet("")]
        public async Task<IActionResult> GetEvents(
            string? search,
            bool isPrivate = false,
            bool isAvailable = true,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var events = await _eventService.GetEvents(search, isPrivate, isAvailable, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    "The events have been fetched successfully.",
                    events.Select(EventMapper.MapToResponse)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return ErrorUtility.HandleError(e);

                Logger.Error($"[EventsController] GetEvents failed: {e}");
                return ErrorUtility.HandleError(e);
            }
        }
    }
}
