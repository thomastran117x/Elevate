using System.ComponentModel.DataAnnotations;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.events.waitlist.contracts.requests;
using backend.main.features.events.waitlist.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.events.waitlist
{
    /// <summary>
    /// Waitlist endpoints. When an event is full and has a waitlist enabled, users queue here
    /// and are promoted into a registration automatically as seats free up.
    /// </summary>
    [ApiController]
    [FeatureGate(FeatureFlagKeys.EventsWaitlist)]
    [Route("events")]
    public class EventWaitlistController : ControllerBase
    {
        private readonly IEventWaitlistService _waitlistService;

        public EventWaitlistController(IEventWaitlistService waitlistService)
        {
            _waitlistService = waitlistService;
        }

        [Authorize]
        [HttpPost("{eventId}/waitlist")]
        [ProducesResponseType(typeof(ApiResponse<EventWaitlistEntryResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> Join(
            [Range(1, int.MaxValue)] int eventId,
            [FromBody] JoinWaitlistRequest? request = null)
        {
            try
            {
                var user = User.GetUserPayload();

                var entry = await _waitlistService.JoinAsync(eventId, user.Id, user.Role, request);

                return StatusCode(201, new ApiResponse<EventWaitlistEntryResponse>(
                    $"You've been added to the waitlist for event with ID {eventId}.",
                    entry
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] Join failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpDelete("{eventId}/waitlist")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Leave([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _waitlistService.LeaveAsync(eventId, user.Id, user.Role);

                return Ok(new MessageResponse(
                    $"You've left the waitlist for event with ID {eventId}."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] Leave failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("{eventId}/waitlist/me")]
        [ProducesResponseType(typeof(ApiResponse<MyWaitlistStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyStatus([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var status = await _waitlistService.GetMyStatusAsync(eventId, user.Id, user.Role);

                return Ok(new ApiResponse<MyWaitlistStatusResponse>(
                    $"Waitlist status for event with ID {eventId} has been fetched successfully.",
                    status
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] GetMyStatus failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("{eventId}/waitlist")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventWaitlistEntryResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWaitlist(
            [Range(1, int.MaxValue)] int eventId,
            [Range(1, int.MaxValue)] int page = 1,
            [Range(1, 100)] int pageSize = 20)
        {
            try
            {
                var user = User.GetUserPayload();

                var (entries, totalCount) = await _waitlistService.GetEventWaitlistAsync(
                    eventId, user.Id, user.Role, page, pageSize);

                return Ok(ApiResponse<IEnumerable<EventWaitlistEntryResponse>>.WithMeta(
                    $"Waitlist for event with ID {eventId} has been fetched successfully.",
                    entries,
                    new
                    {
                        totalCount,
                        page,
                        pageSize
                    }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] GetWaitlist failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpDelete("{eventId}/waitlist/{entryId}")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemoveEntry(
            [Range(1, int.MaxValue)] int eventId,
            [Range(1, int.MaxValue)] int entryId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _waitlistService.RemoveEntryAsync(eventId, entryId, user.Id, user.Role);

                return Ok(new MessageResponse(
                    $"Waitlist entry {entryId} has been removed."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] RemoveEntry failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpPost("{eventId}/waitlist/promote")]
        [ProducesResponseType(typeof(ApiResponse<WaitlistPromotionResultResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PromoteNext([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _waitlistService.PromoteNextAsync(eventId, user.Id, user.Role);

                return Ok(new ApiResponse<WaitlistPromotionResultResponse>(
                    $"Promoted {result.PromotedCount} user(s) from the waitlist.",
                    result
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] PromoteNext failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        // Literal segment declared alongside "{eventId}/..." routes; ASP.NET prefers literals,
        // as the existing events/me/invited route already relies on.
        [Authorize]
        [HttpGet("me/waitlisted")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<WaitlistedEventResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyWaitlists()
        {
            try
            {
                var user = User.GetUserPayload();

                var entries = await _waitlistService.GetMyWaitlistsAsync(user.Id, user.Role);

                return Ok(new ApiResponse<IEnumerable<WaitlistedEventResponse>>(
                    "Your waitlisted events have been fetched successfully.",
                    entries
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventWaitlistController] GetMyWaitlists failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
