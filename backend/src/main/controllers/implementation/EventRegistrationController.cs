using System.ComponentModel.DataAnnotations;

using backend.main.application.security;
using backend.main.dtos.requests.events;
using backend.main.dtos.responses.eventregistration;
using backend.main.dtos.responses.general;
using backend.main.shared.exceptions.http;
using backend.main.models.core;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("events")]
    public class EventRegistrationController : ControllerBase
    {
        private readonly IEventRegistrationService _registrationService;

        public EventRegistrationController(IEventRegistrationService registrationService)
        {
            _registrationService = registrationService;
        }

        [Authorize]
        [HttpPost("{eventId}/register")]
        public async Task<IActionResult> Register([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _registrationService.RegisterAsync(eventId, user.Id);

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
        public async Task<IActionResult> Unregister([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _registrationService.UnregisterAsync(eventId, user.Id);

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

        [HttpGet("{eventId}/registrations")]
        public async Task<IActionResult> GetRegistrations([Range(1, int.MaxValue)] int eventId, int page = 1, int pageSize = 20)
        {
            try
            {
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
        public async Task<IActionResult> CheckRegistration([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                bool isRegistered = await _registrationService.IsRegisteredAsync(eventId, user.Id);

                return Ok(new ApiResponse<object>(
                    $"Registration status for event with ID {eventId} has been fetched successfully.",
                    new { isRegistered }
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
        public async Task<IActionResult> BatchRegister([FromBody] BatchRegistrationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _registrationService.BatchRegisterAsync(user.Id, request.EventIds);

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
        public async Task<IActionResult> BatchUnregister([FromBody] BatchRegistrationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _registrationService.BatchUnregisterAsync(user.Id, request.EventIds);

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
            => new(r.Id, r.UserId, r.EventId, r.CreatedAt);
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
        public async Task<IActionResult> GetRegisteredEvents([Range(1, int.MaxValue)] int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                var registrations = await _registrationService.GetRegistrationsByUserAsync(userId, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<EventRegistrationResponse>>(
                    $"Registered events for user with ID {userId} have been fetched successfully.",
                    registrations.Select(r => new EventRegistrationResponse(r.Id, r.UserId, r.EventId, r.CreatedAt))
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
