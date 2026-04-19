using System.Text.Json;

using backend.main.dtos;
using backend.main.dtos.messages;
using backend.main.dtos.requests.events;
using backend.main.dtos.responses.events;
using backend.main.exceptions.http;
using backend.main.Mappers;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;


namespace backend.main.services.implementation
{
    public class EventsService : IEventsService
    {
        private readonly IEventsRepository _eventsRepository;
        private readonly IEventImageRepository _imageRepository;
        private readonly IClubService _clubService;
        private readonly IAzureBlobService _blobService;
        private readonly ICacheService _cache;
        private readonly IEventAnalyticsRepository _analyticsRepository;
        private readonly IPublisher _publisher;
        private readonly IEventSearchService _searchService;

        private static readonly TimeSpan EventTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EventListTTL = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);
        private const string EventListVersionKey = "events:version";
        private const string NullSentinel = "__null__";

        public EventsService(
            IEventsRepository eventsRepository,
            IEventImageRepository imageRepository,
            IClubService clubService,
            IAzureBlobService blobService,
            ICacheService cache,
            IEventAnalyticsRepository analyticsRepository,
            IPublisher publisher,
            IEventSearchService searchService)
        {
            _eventsRepository = eventsRepository;
            _imageRepository = imageRepository;
            _clubService = clubService;
            _blobService = blobService;
            _cache = cache;
            _analyticsRepository = analyticsRepository;
            _publisher = publisher;
            _searchService = searchService;
        }

        public async Task<Events> CreateEvent(
            int clubId,
            int userId,
            string name,
            string description,
            string location,
            IEnumerable<string> imageUrls,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate = false,
            int maxParticipants = 100,
            int registerCost = 0)
        {
            try
            {
                await _clubService.GetClub(clubId);

                var ev = new Events
                {
                    Name = name,
                    Description = description,
                    Location = location,
                    StartTime = startTime,
                    EndTime = endTime,
                    isPrivate = isPrivate,
                    maxParticipants = maxParticipants,
                    registerCost = registerCost,
                    ClubId = clubId
                };

                var created = await _eventsRepository.CreateAsync(ev);

                await _imageRepository.AddImagesAsync(created.Id, imageUrls.Take(5));

                // Re-fetch with images included so cache is warm with full data
                var withImages = await _eventsRepository.GetByIdAsync(created.Id)
                    ?? throw new InternalServerErrorException("Failed to reload created event");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();
                await PublishIndexEventAsync(BuildUpsertEvent(withImages));

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] CreateEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Events> GetEvent(int eventId)
        {
            try
            {
                var key = $"event:{eventId}";
                var cached = await _cache.GetValueAsync(key);

                if (cached == NullSentinel)
                    throw new ResourceNotFoundException($"Event {eventId} not found");

                if (cached != null)
                    return JsonSerializer.Deserialize<Events>(cached)!;

                var ev = await _eventsRepository.GetByIdAsync(eventId);
                if (ev == null)
                {
                    await _cache.SetValueAsync(key, NullSentinel, NotFoundTTL);
                    throw new ResourceNotFoundException($"Event {eventId} not found");
                }

                await CacheEventAsync(ev);
                return ev;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<List<Events>> GetEvents(
            string? search = null,
            bool isPrivate = false,
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var version = await GetEventListVersionAsync();

                var normalized = search?.Trim().ToLowerInvariant();
                var statusKey = status.HasValue ? status.Value.ToString() : "all";

                var key = normalized == null
                    ? $"events:list:v{version}:st{statusKey}:p{page}:s{pageSize}"
                    : $"events:list:v{version}:st{statusKey}:q:{normalized}:p{page}:s{pageSize}";

                var cached = await _cache.GetValueAsync(key);
                if (cached != null)
                    return JsonSerializer.Deserialize<List<Events>>(cached)!;

                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    try
                    {
                        var (ids, _) = await _searchService.SearchAsync(normalized, isPrivate, status, page, pageSize);
                        var esEvents = ids.Count > 0
                            ? await _eventsRepository.GetByIdsAsync(ids)
                            : new List<Events>();

                        var ordered = ids
                            .Select(id => esEvents.FirstOrDefault(e => e.Id == id))
                            .Where(e => e != null)
                            .Cast<Events>()
                            .ToList();

                        await _cache.SetValueAsync(key, JsonSerializer.Serialize(ordered), WithJitter(EventListTTL));
                        return ordered;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "[EventsService] Elasticsearch unavailable. Falling back to MySQL LIKE search.");
                    }
                }

                var events = await _eventsRepository.SearchAsync(
                    normalized,
                    isPrivate,
                    status,
                    page,
                    pageSize
                );

                await _cache.SetValueAsync(
                    key,
                    JsonSerializer.Serialize(events),
                    WithJitter(EventListTTL)
                );

                return events;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<List<Events>> GetEventsByClub(
            int clubId,
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                await _clubService.GetClub(clubId);

                return await _eventsRepository.SearchAsync(
                    null,
                    false,
                    status,
                    page,
                    pageSize
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEventsByClub failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Events> UpdateEvent(
            int eventId,
            int userId,
            string name,
            string description,
            string location,
            IEnumerable<string>? imageUrls,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate,
            int maxParticipants,
            int registerCost)
        {
            try
            {
                var evTask = GetEvent(eventId);
                var clubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(evTask, clubTask);

                var existing = await evTask;
                var club = await clubTask;

                if (existing.ClubId != club.Id)
                    throw new ForbiddenException("Not allowed");

                var updated = await _eventsRepository.UpdateAsync(eventId, new Events
                {
                    Name = name,
                    Description = description,
                    Location = location,
                    StartTime = startTime,
                    EndTime = endTime,
                    isPrivate = isPrivate,
                    maxParticipants = maxParticipants,
                    registerCost = registerCost,
                    ClubId = club.Id
                }) ?? throw new InternalServerErrorException("Update failed");

                if (imageUrls != null)
                {
                    var urlList = imageUrls.Take(5).ToList();
                    var oldUrls = existing.Images.Select(i => i.ImageUrl).ToList();

                    // Best-effort Azure blob deletion
                    _ = Task.WhenAll(oldUrls.Select(u => _blobService.DeleteBlobAsync(u)));

                    await _imageRepository.DeleteAllByEventIdAsync(eventId);
                    await _imageRepository.AddImagesAsync(eventId, urlList);
                }

                // Re-fetch with fresh images for cache
                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload updated event");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();
                await PublishIndexEventAsync(BuildUpsertEvent(withImages));

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] UpdateEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task DeleteEvent(int eventId, int userId)
        {
            try
            {
                var evTask = GetEvent(eventId);
                var clubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(evTask, clubTask);

                var ev = await evTask;
                var club = await clubTask;

                if (ev.ClubId != club.Id)
                    throw new ForbiddenException("Not allowed");

                if (!await _eventsRepository.DeleteAsync(eventId))
                    throw new InternalServerErrorException("Delete failed");

                // EventImages cascade-deletes via FK; clean up blobs best-effort
                var urls = ev.Images.Select(i => i.ImageUrl).ToList();
                _ = Task.WhenAll(urls.Select(u => _blobService.DeleteBlobAsync(u)));

                await _cache.DeleteKeyAsync($"event:{eventId}");
                await BumpEventListVersionAsync();
                await PublishIndexEventAsync(new EventIndexEvent { Operation = "delete", EventId = eventId });
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] DeleteEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<List<Events>> GetEventsByIds(IEnumerable<int> ids)
        {
            try
            {
                return await _eventsRepository.GetByIdsAsync(ids);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEventsByIds failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<BatchCreateResultResponse> BatchCreateEvents(
            int clubId,
            int userId,
            IEnumerable<BatchCreateEventItem> items)
        {
            try
            {
                var club = await _clubService.GetClubByUser(userId);

                if (club.Id != clubId)
                    throw new ForbiddenException("Not allowed");

                var itemList = items.ToList();
                var entities = itemList.Select(item => new Events
                {
                    Name = item.Name,
                    Description = item.Description,
                    Location = item.Location,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    isPrivate = item.IsPrivate,
                    maxParticipants = item.MaxParticipants,
                    registerCost = item.RegisterCost,
                    ClubId = clubId
                }).ToList();

                var created = await _eventsRepository.CreateManyAsync(entities);

                foreach (var (ev, item) in created.Zip(itemList))
                    await _imageRepository.AddImagesAsync(ev.Id, item.ImageUrls.Take(5));

                await BumpEventListVersionAsync();

                foreach (var ev in created)
                    await PublishIndexEventAsync(BuildUpsertEvent(ev));

                return new BatchCreateResultResponse
                {
                    Created = created.Select(EventMapper.MapToResponse).ToList()
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] BatchCreateEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<int> BatchUpdateEvents(int userId, IEnumerable<BatchUpdateEventItem> items)
        {
            try
            {
                var club = await _clubService.GetClubByUser(userId);
                var itemList = items.ToList();
                var ids = itemList.Select(i => i.EventId).ToList();

                var existing = await _eventsRepository.GetByIdsAsync(ids);

                if (existing.Any(ev => ev.ClubId != club.Id))
                    throw new ForbiddenException("One or more events do not belong to your club");

                var patches = itemList.Select(item => (
                    id: item.EventId,
                    patch: (Action<Events>)(ev =>
                    {
                        if (item.Name != null) ev.Name = item.Name;
                        if (item.Description != null) ev.Description = item.Description;
                        if (item.Location != null) ev.Location = item.Location;
                        if (item.IsPrivate.HasValue) ev.isPrivate = item.IsPrivate.Value;
                        if (item.MaxParticipants.HasValue) ev.maxParticipants = item.MaxParticipants.Value;
                        if (item.RegisterCost.HasValue) ev.registerCost = item.RegisterCost.Value;
                        if (item.StartTime.HasValue) ev.StartTime = item.StartTime.Value;
                        if (item.EndTime.HasValue) ev.EndTime = item.EndTime.Value;
                    })
                ));

                var count = await _eventsRepository.UpdateManyAsync(patches);

                await Task.WhenAll(ids.Select(id => _cache.DeleteKeyAsync($"event:{id}")));
                await BumpEventListVersionAsync();

                var updated = await _eventsRepository.GetByIdsAsync(ids);
                foreach (var ev in updated)
                    await PublishIndexEventAsync(BuildUpsertEvent(ev));

                return count;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] BatchUpdateEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<int> BatchDeleteEvents(int userId, IEnumerable<int> ids)
        {
            try
            {
                var club = await _clubService.GetClubByUser(userId);
                var idList = ids.ToList();

                var existing = await _eventsRepository.GetByIdsAsync(idList);

                if (existing.Any(ev => ev.ClubId != club.Id))
                    throw new ForbiddenException("One or more events do not belong to your club");

                var imageUrls = existing
                    .SelectMany(ev => ev.Images.Select(i => i.ImageUrl))
                    .ToList();

                var deleted = await _eventsRepository.DeleteManyAsync(idList);

                _ = Task.WhenAll(imageUrls.Select(url => _blobService.DeleteBlobAsync(url)));
                await Task.WhenAll(idList.Select(id => _cache.DeleteKeyAsync($"event:{id}")));
                await BumpEventListVersionAsync();

                foreach (var id in idList)
                    await PublishIndexEventAsync(new EventIndexEvent { Operation = "delete", EventId = id });

                return deleted;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] BatchDeleteEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<PresignedUploadResponse> GenerateImageUploadUrlAsync(
            string fileName, string contentType)
        {
            try
            {
                return await _blobService.GenerateUploadUrlAsync(fileName, contentType);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GenerateImageUploadUrlAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<EventImage> AddEventImageAsync(int eventId, int userId, string imageUrl)
        {
            try
            {
                var evTask = GetEvent(eventId);
                var clubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(evTask, clubTask);

                var ev = await evTask;
                var club = await clubTask;

                if (ev.ClubId != club.Id)
                    throw new ForbiddenException("Not allowed");

                var count = await _imageRepository.CountByEventIdAsync(eventId);
                if (count >= 5)
                    throw new BadRequestException("An event cannot have more than 5 images.");

                var images = await _imageRepository.AddImagesAsync(eventId, new[] { imageUrl });

                await _cache.DeleteKeyAsync($"event:{eventId}");
                await BumpEventListVersionAsync();

                return images[0];
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] AddEventImageAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RemoveEventImageAsync(int eventId, int imageId, int userId)
        {
            try
            {
                var evTask = GetEvent(eventId);
                var clubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(evTask, clubTask);

                var ev = await evTask;
                var club = await clubTask;

                if (ev.ClubId != club.Id)
                    throw new ForbiddenException("Not allowed");

                var image = await _imageRepository.GetByIdAsync(imageId, eventId)
                    ?? throw new ResourceNotFoundException(
                        $"Image {imageId} not found on event {eventId}");

                await _imageRepository.DeleteImageAsync(imageId, eventId);

                _ = _blobService.DeleteBlobAsync(image.ImageUrl);

                await _cache.DeleteKeyAsync($"event:{eventId}");
                await BumpEventListVersionAsync();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] RemoveEventImageAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<EventAnalyticsResponse> GetEventAnalytics(int eventId, int userId)
        {
            try
            {
                var evTask = GetEvent(eventId);
                var clubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(evTask, clubTask);

                var ev = await evTask;
                var club = await clubTask;

                if (ev.ClubId != club.Id)
                    throw new ForbiddenException("Not allowed");

                var data = await _analyticsRepository.GetEventAnalyticsAsync(eventId);

                var fillRate = ev.maxParticipants > 0
                    ? Math.Round(data.RegistrationCount / (double)ev.maxParticipants * 100.0, 2)
                    : 0.0;

                return new EventAnalyticsResponse
                {
                    EventId = ev.Id,
                    EventName = ev.Name,
                    RegistrationCount = data.RegistrationCount,
                    MaxParticipants = ev.maxParticipants,
                    FillRate = fillRate,
                    SpotsRemaining = Math.Max(0, ev.maxParticipants - data.RegistrationCount),
                    TotalRevenue = data.TotalRevenue,
                    PendingRevenue = data.PendingRevenue,
                    RefundedAmount = data.RefundedAmount,
                    RegistrationsToday = data.RegistrationsToday,
                    RegistrationsThisWeek = data.RegistrationsThisWeek,
                    RegistrationsThisMonth = data.RegistrationsThisMonth
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEventAnalytics failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<ClubAnalyticsResponse> GetClubAnalytics(int clubId, int userId)
        {
            try
            {
                var clubTask = _clubService.GetClub(clubId);
                var callerClubTask = _clubService.GetClubByUser(userId);

                await Task.WhenAll(clubTask, callerClubTask);

                var club = await clubTask;
                var callerClub = await callerClubTask;

                if (club.Id != callerClub.Id)
                    throw new ForbiddenException("Not allowed");

                var data = await _analyticsRepository.GetClubAnalyticsAsync(clubId);

                static TopEventEntry ToTopEntry(PerEventAnalytics e) => new()
                {
                    Id = e.EventId,
                    Name = e.EventName,
                    RegistrationCount = e.RegistrationCount,
                    FillRate = e.MaxParticipants > 0
                        ? Math.Round(e.RegistrationCount / (double)e.MaxParticipants * 100.0, 2)
                        : 0.0,
                    Revenue = e.Revenue
                };

                var topByRegistrations = data.PerEvent
                    .OrderByDescending(e => e.RegistrationCount)
                    .Take(5)
                    .Select(ToTopEntry)
                    .ToList();

                var topByRevenue = data.PerEvent
                    .OrderByDescending(e => e.Revenue)
                    .Take(5)
                    .Select(ToTopEntry)
                    .ToList();

                var topByFillRate = data.PerEvent
                    .OrderByDescending(e => e.MaxParticipants > 0
                        ? e.RegistrationCount / (double)e.MaxParticipants
                        : 0.0)
                    .Take(5)
                    .Select(ToTopEntry)
                    .ToList();

                var avgFillRate = data.PerEvent.Count > 0
                    ? Math.Round(data.PerEvent.Average(e => e.MaxParticipants > 0
                        ? e.RegistrationCount / (double)e.MaxParticipants * 100.0
                        : 0.0), 2)
                    : 0.0;

                return new ClubAnalyticsResponse
                {
                    ClubId = clubId,
                    TotalEvents = data.TotalEvents,
                    UpcomingEvents = data.UpcomingEvents,
                    OngoingEvents = data.OngoingEvents,
                    PastEvents = data.PastEvents,
                    TotalRegistrations = data.TotalRegistrations,
                    UniqueAttendees = data.UniqueAttendees,
                    RepeatAttendees = data.RepeatAttendees,
                    TotalRevenue = data.TotalRevenue,
                    PendingRevenue = data.PendingRevenue,
                    AvgFillRate = avgFillRate,
                    TopEventsByRegistrations = topByRegistrations,
                    TopEventsByRevenue = topByRevenue,
                    TopEventsByFillRate = topByFillRate,
                    RegistrationTrend = data.DailyTrend
                        .Select(d => new DailyRegistrationEntry { Date = d.Date, Count = d.Count })
                        .ToList(),
                    RevenueTrend = data.RevenueTrend
                        .Select(d => new DailyRevenueEntry { Date = d.Date, Amount = d.Amount })
                        .ToList()
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetClubAnalytics failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private static EventIndexEvent BuildUpsertEvent(Events ev) => new()
        {
            Operation = "upsert",
            EventId = ev.Id,
            ClubId = ev.ClubId,
            Name = ev.Name,
            Description = ev.Description,
            Location = ev.Location,
            IsPrivate = ev.isPrivate,
            StartTime = ev.StartTime,
            EndTime = ev.EndTime,
            CreatedAt = ev.CreatedAt,
            UpdatedAt = ev.UpdatedAt
        };

        private async Task PublishIndexEventAsync(EventIndexEvent evt)
        {
            try
            {
                await _publisher.PublishAsync("event-es-index", evt);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to publish ES index event for event {evt.EventId}. Index may be stale.");
            }
        }

        private async Task CacheEventAsync(Events ev)
        {
            await _cache.SetValueAsync(
                $"event:{ev.Id}",
                JsonSerializer.Serialize(ev),
                WithJitter(EventTTL)
            );
        }

        private async Task<long> GetEventListVersionAsync()
        {
            var v = await _cache.GetValueAsync(EventListVersionKey);
            if (v == null)
            {
                await _cache.SetValueAsync(EventListVersionKey, "1");
                return 1;
            }
            return long.Parse(v);
        }

        private async Task BumpEventListVersionAsync()
        {
            await _cache.IncrementAsync(EventListVersionKey);
        }

        private static TimeSpan WithJitter(TimeSpan baseTtl, int percent = 20)
        {
            var delta = Random.Shared.Next(-percent, percent + 1);
            return baseTtl + TimeSpan.FromMilliseconds(
                baseTtl.TotalMilliseconds * delta / 100.0
            );
        }
    }
}
