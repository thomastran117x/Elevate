using System.ComponentModel.DataAnnotations;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.events.favourites.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.events.favourites
{
    /// <summary>
    /// Favourite ("star") endpoints. A star is a lightweight save-for-later that consumes no
    /// seat and collects no details, sitting between ignoring an event and registering for it.
    /// </summary>
    [ApiController]
    [FeatureGate(FeatureFlagKeys.EventsFavourites)]
    [Route("events")]
    public class EventFavouriteController : ControllerBase
    {
        private readonly IEventFavouriteService _favouriteService;

        public EventFavouriteController(IEventFavouriteService favouriteService)
        {
            _favouriteService = favouriteService;
        }

        [Authorize]
        [HttpPost("{eventId}/favourite")]
        [ProducesResponseType(typeof(ApiResponse<EventFavouriteResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> Favourite([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var favourite = await _favouriteService.FavouriteAsync(eventId, user.Id, user.Role);

                return StatusCode(201, new ApiResponse<EventFavouriteResponse>(
                    $"Event with ID {eventId} has been saved to your favourites.",
                    favourite
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventFavouriteController] Favourite failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpDelete("{eventId}/favourite")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Unfavourite([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _favouriteService.UnfavouriteAsync(eventId, user.Id);

                return Ok(new MessageResponse(
                    $"Event with ID {eventId} has been removed from your favourites."
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventFavouriteController] Unfavourite failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("{eventId}/favourite/me")]
        [ProducesResponseType(typeof(ApiResponse<EventFavouriteResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyStatus([Range(1, int.MaxValue)] int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var status = await _favouriteService.GetMyStatusAsync(eventId, user.Id);

                return Ok(new ApiResponse<EventFavouriteResponse>(
                    $"Favourite status for event with ID {eventId} has been fetched successfully.",
                    status
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventFavouriteController] GetMyStatus failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        // Literal segments declared alongside the "{eventId}/..." routes; ASP.NET prefers
        // literals, as the existing events/me/invited and events/me/waitlisted routes rely on.
        [Authorize]
        [HttpGet("me/favourites/ids")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<int>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyFavouriteIds()
        {
            try
            {
                var user = User.GetUserPayload();

                var eventIds = await _favouriteService.GetFavouriteEventIdsAsync(user.Id);

                return Ok(new ApiResponse<IEnumerable<int>>(
                    "Your favourited event IDs have been fetched successfully.",
                    eventIds
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventFavouriteController] GetMyFavouriteIds failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("me/pinned")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<PinnedEventResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyPinned()
        {
            try
            {
                var user = User.GetUserPayload();

                var pinned = await _favouriteService.GetMyPinnedAsync(user.Id, user.Role);

                return Ok(new ApiResponse<IEnumerable<PinnedEventResponse>>(
                    "Your pinned events have been fetched successfully.",
                    pinned
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventFavouriteController] GetMyPinned failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
