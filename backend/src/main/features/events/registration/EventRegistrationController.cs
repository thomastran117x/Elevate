using System.ComponentModel.DataAnnotations;

using backend.main.application.security;
using backend.main.features.events;
using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.requests;
using backend.main.features.events.registration.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.events.registration
{
    /// <summary>
    /// Registration endpoints for event attendance and registration status lookups.
    /// </summary>
    [ApiController]
    [Route("events")]
    public class EventRegistrationController : ControllerBase
    {
        private readonly IEventRegistrationService _registrationService;
        private readonly IEventsService _eventsService;

        public EventRegistrationController(IEventRegistrationService registrationService, IEventsService eventsService)
        {
            _registrationService = registrationService;
            _eventsService = eventsService;
        }

        [Authorize]
        [HttpPost("{eventId}/register")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> Register(
            [Range(1, int.MaxValue)] int eventId,
            [FromBody] RegisterEventRequest? request = null)
        {
            try
            {
                var user = User.GetUserPayload();

                await _registrationService.RegisterAsync(eventId, user.Id, user.Role, request);

                return StatusCode(201, new MessageResponse(
                    $"Successfully registered for event with ID {eventId}."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] Register failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpDelete("{eventId}/register")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Unregister([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _registrationService.UnregisterAsync(eventId, user.Id, user.Role);

                return Ok(new MessageResponse(
                    $"Successfully unregistered from event with ID {eventId}."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] Unregister failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpPatch("{eventId}/register")]
        [ProducesResponseType(typeof(ApiResponse<EventRegistrationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateRegistration(
            [Range(1, int.MaxValue)] int eventId,
            [FromBody] UpdateRegistrationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var registration = await _registrationService.UpdateRegistrationAsync(eventId, user.Id, user.Role, request);

                return Ok(new ApiResponse<EventRegistrationResponse>(
                    $"Registration details for event with ID {eventId} have been updated.",
                    MapToResponse(registration)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] UpdateRegistration failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("{eventId}/registrations")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventRegistrationResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRegistrations([Range(1, int.MaxValue)] int eventId, int page = 1, int pageSize = 20)
        {
            try
            {
                var user = GetOptionalUserPayload();
                await _eventsService.GetVisibleEvent(eventId, user?.Id, user?.Role);
                var registrations = await _registrationService.GetRegistrationsByEventAsync(eventId, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<EventRegistrationResponse>>(
                    $"Registrations for event with ID {eventId} have been fetched successfully.",
                    registrations.Select(MapToResponse)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] GetRegistrations failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("{eventId}/registrations/me")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CheckRegistration([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                bool isRegistered = await _registrationService.IsRegisteredAsync(eventId, user.Id, user.Role);

                return Ok(new ApiResponse<object>(
                    $"Registration status for event with ID {eventId} has been fetched successfully.",
                    new
                    {
                        isRegistered
                    }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] CheckRegistration failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpPost("batch/register")]
        [ProducesResponseType(typeof(ApiResponse<BatchRegistrationResultResponse>), StatusCodes.Status207MultiStatus)]
        public async Task<IActionResult> BatchRegister([FromBody] BatchRegistrationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _registrationService.BatchRegisterAsync(user.Id, user.Role, request.EventIds);

                return StatusCode(207, new ApiResponse<BatchRegistrationResultResponse>(
                    $"{result.Succeeded.Count} registration(s) succeeded, {result.Failed.Count} failed.",
                    result
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] BatchRegister failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpDelete("batch/register")]
        [ProducesResponseType(typeof(ApiResponse<BatchRegistrationResultResponse>), StatusCodes.Status207MultiStatus)]
        public async Task<IActionResult> BatchUnregister([FromBody] BatchRegistrationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _registrationService.BatchUnregisterAsync(user.Id, user.Role, request.EventIds);

                return StatusCode(207, new ApiResponse<BatchRegistrationResultResponse>(
                    $"{result.Succeeded.Count} unregistration(s) succeeded, {result.Failed.Count} failed.",
                    result
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventRegistrationController] BatchUnregister failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        private static EventRegistrationResponse MapToResponse(EventRegistration r)
            => new(r.Id, r.UserId, r.EventId, r.CreatedAt, r.Status, r.CancelledAt, r.Notes, r.PhoneNumber, r.DietaryNeeds);

        private UserIdentityPayload? GetOptionalUserPayload()
        {
            if (User.Identity?.IsAuthenticated != true)
                return null;

            return User.GetUserPayload();
        }
    }

    [ApiController]
    [Route("users")]
    public class UserEventRegistrationController : ControllerBase
    {
        private readonly IEventRegistrationService _registrationService;

        public UserEventRegistrationController(IEventRegistrationService registrationService)
        {
            _registrationService = registrationService;
        }

        [Authorize]
        [HttpGet("{userId}/events/registered")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventRegistrationResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRegisteredEvents([Range(1, int.MaxValue)] int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                var registrations = await _registrationService.GetRegistrationsByUserAsync(userId, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<EventRegistrationResponse>>(
                    $"Registered events for user with ID {userId} have been fetched successfully.",
                    registrations.Select(r => new EventRegistrationResponse(r.Id, r.UserId, r.EventId, r.CreatedAt, r.Status, r.CancelledAt, r.Notes, r.PhoneNumber, r.DietaryNeeds))
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserEventRegistrationController] GetRegisteredEvents failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
