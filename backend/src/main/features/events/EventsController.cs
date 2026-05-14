using backend.main.application.security;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.contracts.responses;
using backend.main.shared.responses;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.events.versions.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.features.events;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using backend.main.shared.utilities.logger;
using backend.main.features.clubs;

namespace backend.main.features.events
{
    [ApiController]
    [Route("events")]
    public class EventsController : ControllerBase
    {
        private readonly IEventsService _eventService;
        private readonly IClubService _clubService;

        public EventsController(IEventsService eventService, IClubService clubService)
        {
            _eventService = eventService;
            _clubService = clubService;
        }

        [Authorize]
        [HttpPost("{clubId}")]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateRequest request, int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var ev = await _eventService.CreateEvent(
                    clubId,
                    user.Id,
                    user.Role,
                    request.Name,
                    request.Description,
                    request.Location,
                    request.ImageUrls,
                    request.StartTime,
                    request.EndTime,
                    request.IsPrivate,
                    request.MaxParticipants,
                    request.RegisterCost,
                    request.Category,
                    request.VenueName,
                    request.City,
                    request.Latitude,
                    request.Longitude,
                    request.Tags
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

        [Authorize]
        [HttpPut("{eventId}")]
        public async Task<IActionResult> UpdateEvent([FromBody] EventUpdateRequest request, int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var ev = await _eventService.UpdateEvent(
                    eventId,
                    user.Id,
                    user.Role,
                    request.Name,
                    request.Description,
                    request.Location,
                    request.ImageUrls,
                    request.StartTime,
                    request.EndTime,
                    request.IsPrivate,
                    request.MaxParticipants,
                    request.RegisterCost,
                    request.Category,
                    request.VenueName,
                    request.City,
                    request.Latitude,
                    request.Longitude,
                    request.Tags
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

        [Authorize]
        [HttpDelete("{eventId}")]
        public async Task<IActionResult> DeleteEvent(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _eventService.DeleteEvent(eventId, user.Id, user.Role);

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

        [Authorize]
        [HttpGet("{eventId}/versions")]
        public async Task<IActionResult> GetEventVersions(
            int eventId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var effectivePage = page < 1 ? 1 : page;
            var effectivePageSize = pageSize switch
            {
                < 1 => 20,
                > 100 => 100,
                _ => pageSize
            };

            var userPayload = User.GetUserPayload();
            var (items, totalCount) = await _eventService.GetVersionHistoryAsync(
                eventId,
                userPayload.Id,
                userPayload.Role,
                effectivePage,
                effectivePageSize);

            var paged = new PagedResponse<EventVersionListItemResponse>(
                items.Select(MapToVersionListItemResponse),
                totalCount,
                effectivePage,
                effectivePageSize);

            return Ok(new ApiResponse<PagedResponse<EventVersionListItemResponse>>(
                $"Version history for event with ID {eventId} has been fetched successfully.",
                paged
            ));
        }

        [Authorize]
        [HttpGet("{eventId}/versions/{versionNumber}")]
        public async Task<IActionResult> GetEventVersion(int eventId, int versionNumber)
        {
            var userPayload = User.GetUserPayload();
            var version = await _eventService.GetVersionDetailAsync(
                eventId,
                versionNumber,
                userPayload.Id,
                userPayload.Role);

            return Ok(new ApiResponse<EventVersionDetailResponse>(
                $"Version {versionNumber} for event with ID {eventId} has been fetched successfully.",
                MapToVersionDetailResponse(version)
            ));
        }

        [Authorize]
        [HttpPost("{eventId}/versions/{versionNumber}/rollback")]
        public async Task<IActionResult> RollbackEventVersion(int eventId, int versionNumber)
        {
            var userPayload = User.GetUserPayload();
            var result = await _eventService.RollbackToVersionAsync(
                eventId,
                versionNumber,
                userPayload.Id,
                userPayload.Role);

            var response = new EventRollbackResponse(
                EventMapper.MapToResponse(result.Event),
                result.RestoredFromVersionNumber,
                result.NewVersionNumber);

            return Ok(new ApiResponse<EventRollbackResponse>(
                $"Event with ID {eventId} has been rolled back to version {versionNumber} successfully.",
                response
            ));
        }

        [HttpGet("clubs/{clubId}")]
        public async Task<IActionResult> GetEventsByClub(int clubId, EventStatus? status = null, int page = 1, int pageSize = 20)
        {
            try
            {
                if (page < 1)
                    return BadRequestResponse("page must be at least 1.");

                if (pageSize < 1 || pageSize > 100)
                    return BadRequestResponse("pageSize must be between 1 and 100.");

                var (events, totalCount, source) = await _eventService.GetEventsByClub(clubId, status: status, page: page, pageSize: pageSize);

                var paged = new PagedResponse<EventResponse>(
                    events.Select(e => EventMapper.MapToResponse(e)),
                    totalCount,
                    page,
                    pageSize
                );

                return Ok(new ApiResponse<PagedResponse<EventResponse>>(
                    $"The events for club {clubId} have been fetched successfully.",
                    paged,
                    source
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
                var user = GetOptionalUserPayload();
                var ev = await _eventService.GetVisibleEvent(eventId, user?.Id, user?.Role);
                var club = await _clubService.GetClub(ev.ClubId);
                var response = EventMapper.MapToResponse(ev);
                response.Club = EventMapper.MapClubToResponse(club);

                return Ok(new ApiResponse<EventResponse>(
                    $"The event with ID {eventId} has been fetched successfully.",
                    response
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
            string? search = null,
            bool isPrivate = false,
            EventStatus? status = null,
            EventCategory? category = null,
            string? tags = null,
            string? city = null,
            double? lat = null,
            double? lng = null,
            double? radiusKm = null,
            EventSortBy sortBy = EventSortBy.Relevance,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var criteria = PublicEventSearchCriteriaFactory.FromQuery(
                    search,
                    isPrivate,
                    status,
                    category,
                    tags,
                    city,
                    lat,
                    lng,
                    radiusKm,
                    sortBy,
                    page,
                    pageSize);

                var (events, totalCount, distances, source) = await _eventService.GetEvents(criteria);

                var paged = new PagedResponse<EventResponse>(
                    events.Select(e => EventMapper.MapToResponse(
                        e,
                        distances.TryGetValue(e.Id, out var d) ? d : (double?)null)),
                    totalCount,
                    page,
                    pageSize
                );

                return Ok(new ApiResponse<PagedResponse<EventResponse>>(
                    "The events have been fetched successfully.",
                    paged,
                    source
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

        [HttpPost("search")]
        public async Task<IActionResult> SearchEvents([FromBody] EventSearchRequest request)
        {
            try
            {
                var criteria = PublicEventSearchCriteriaFactory.FromRequest(request);

                var (events, totalCount, distances, source) = await _eventService.GetEvents(criteria);

                var paged = new PagedResponse<EventResponse>(
                    events.Select(e => EventMapper.MapToResponse(
                        e,
                        distances.TryGetValue(e.Id, out var d) ? d : (double?)null)),
                    totalCount,
                    request.Page,
                    request.PageSize
                );

                return Ok(new ApiResponse<PagedResponse<EventResponse>>(
                    "The events have been fetched successfully.",
                    paged,
                    source
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[EventsController] SearchEvents failed: {e}");
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
                    return BadRequestResponse("No valid IDs provided.");

                var user = GetOptionalUserPayload();
                var events = await _eventService.GetVisibleEventsByIds(parsedIds, user?.Id, user?.Role);

                return Ok(new ApiResponse<IEnumerable<EventResponse>>(
                    $"{events.Count} event(s) fetched successfully.",
                    events.Select(e => EventMapper.MapToResponse(e))
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

        private UserIdentityPayload? GetOptionalUserPayload()
        {
            if (User.Identity?.IsAuthenticated != true)
                return null;

            return User.GetUserPayload();
        }

        private static IActionResult BadRequestResponse(string message) =>
            HandleError.Resolve(new BadRequestException(message));

        [Authorize]
        [HttpPost("batch/{clubId}")]
        public async Task<IActionResult> BatchCreateEvents([FromBody] BatchCreateEventRequest request, int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _eventService.BatchCreateEvents(clubId, user.Id, user.Role, request.Events);

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

        [Authorize]
        [HttpPut("batch")]
        public async Task<IActionResult> BatchUpdateEvents([FromBody] BatchUpdateEventRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var count = await _eventService.BatchUpdateEvents(user.Id, user.Role, request.Events);

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

        [Authorize]
        [HttpDelete("batch")]
        public async Task<IActionResult> BatchDeleteEvents([FromBody] BatchDeleteRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var count = await _eventService.BatchDeleteEvents(user.Id, user.Role, request.Ids);

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

        [Authorize]
        [HttpGet("{eventId}/analytics")]
        public async Task<IActionResult> GetEventAnalytics(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var analytics = await _eventService.GetEventAnalytics(eventId, user.Id, user.Role);

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

        [Authorize]
        [HttpPost("images/presigned-url")]
        public async Task<IActionResult> GetPresignedUploadUrl([FromBody] PresignedUrlRequest request)
        {
            try
            {
                var user = User.GetUserPayload();

                var result = await _eventService.GenerateImageUploadUrlAsync(
                    request.ClubId,
                    user.Id,
                    user.Role,
                    request.FileName,
                    request.ContentType,
                    request.EventId);

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

        [Authorize]
        [HttpPost("{eventId}/images")]
        public async Task<IActionResult> AddEventImage([FromBody] AddEventImageRequest request, int eventId)
        {
            try
            {
                var user = User.GetUserPayload();

                var image = await _eventService.AddEventImageAsync(eventId, user.Id, user.Role, request.ImageUrl);

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

        [Authorize]
        [HttpDelete("{eventId}/images/{imageId}")]
        public async Task<IActionResult> RemoveEventImage(int eventId, int imageId)
        {
            try
            {
                var user = User.GetUserPayload();

                await _eventService.RemoveEventImageAsync(eventId, imageId, user.Id, user.Role);

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

        [Authorize]
        [HttpGet("clubs/{clubId}/analytics")]
        public async Task<IActionResult> GetClubAnalytics(int clubId)
        {
            try
            {
                var user = User.GetUserPayload();

                var analytics = await _eventService.GetClubAnalytics(clubId, user.Id, user.Role);

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

        private static EventVersionListItemResponse MapToVersionListItemResponse(EventVersionHistoryItem item) =>
            new(
                item.VersionNumber,
                item.ActionType,
                item.CreatedAt,
                item.ActorUserId,
                item.ActorRole,
                item.RollbackEligible,
                item.RollbackExpiresAt,
                item.RollbackSourceVersionNumber,
                item.ChangedFields.Select(MapToFieldChangeResponse).ToList()
            );

        private static EventVersionDetailResponse MapToVersionDetailResponse(EventVersionDetail detail) =>
            new(
                detail.VersionNumber,
                detail.ActionType,
                detail.CreatedAt,
                detail.ActorUserId,
                detail.ActorRole,
                detail.RollbackEligible,
                detail.RollbackExpiresAt,
                detail.RollbackSourceVersionNumber,
                detail.ChangedFields.Select(MapToFieldChangeResponse).ToList(),
                new EventVersionSnapshotResponse(
                    detail.Snapshot.Name,
                    detail.Snapshot.Description,
                    detail.Snapshot.Location,
                    detail.Snapshot.IsPrivate,
                    detail.Snapshot.MaxParticipants,
                    detail.Snapshot.RegisterCost,
                    detail.Snapshot.StartTime,
                    detail.Snapshot.EndTime,
                    detail.Snapshot.ClubId,
                    detail.Snapshot.Category,
                    detail.Snapshot.VenueName,
                    detail.Snapshot.City,
                    detail.Snapshot.Latitude,
                    detail.Snapshot.Longitude,
                    detail.Snapshot.Tags
                )
            );

        private static EventVersionFieldChangeResponse MapToFieldChangeResponse(EventVersionFieldChange change) =>
            new(change.Field, change.OldValue, change.NewValue);
    }

    [ApiController]
    [Route("admin/events")]
    [Authorize("AdminOnly")]
    public class AdminEventsController : ControllerBase
    {
        private readonly IEventReindexService _reindexService;

        public AdminEventsController(IEventReindexService reindexService)
        {
            _reindexService = reindexService;
        }

        [HttpPost("reindex")]
        public async Task<IActionResult> ReindexEvents(CancellationToken cancellationToken)
        {
            var count = await _reindexService.ReindexAllAsync(cancellationToken);
            return Ok(new ApiResponse<object>(
                "Events reindexed successfully.",
                new { indexed = count }
            ));
        }
    }
}


