using System.Text.Json;
using System.Security.Cryptography;

using backend.main.infrastructure.database.core;
using backend.main.shared.responses;
using backend.main.features.events.analytics;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.shared.exceptions.http;
using backend.main.features.clubs;
using backend.main.features.cache;
using backend.main.shared.storage;

using Microsoft.EntityFrameworkCore;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.utilities.logger;


namespace backend.main.features.events
{
    public class EventsService : IEventsService
    {
        private readonly AppDatabaseContext _db;
        private readonly IEventsRepository _eventsRepository;
        private readonly IEventImageRepository _imageRepository;
        private readonly IClubService _clubService;
        private readonly IAzureBlobService _blobService;
        private readonly ICacheService _cache;
        private readonly IEventAnalyticsRepository _analyticsRepository;
        private readonly IEventSearchService _searchService;
        private readonly IEventSearchOutboxWriter _outboxWriter;
        private readonly IEventRegistrationRepository _registrationRepository;

        private static readonly TimeSpan EventTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EventListTTL = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ImageUploadIntentTTL = TimeSpan.FromMinutes(20);
        private const string EventListVersionKey = "events:version";
        private const string NullSentinel = "__null__";

        private sealed record EventImageUploadIntent(
            int ClubId,
            int? EventId,
            int UserId,
            string PublicUrl,
            string ContentType
        );

        public EventsService(
            AppDatabaseContext db,
            IEventsRepository eventsRepository,
            IEventImageRepository imageRepository,
            IClubService clubService,
            IAzureBlobService blobService,
            ICacheService cache,
            IEventAnalyticsRepository analyticsRepository,
            IEventSearchService searchService,
            IEventSearchOutboxWriter outboxWriter,
            IEventRegistrationRepository registrationRepository)
        {
            _db = db;
            _eventsRepository = eventsRepository;
            _imageRepository = imageRepository;
            _clubService = clubService;
            _blobService = blobService;
            _cache = cache;
            _analyticsRepository = analyticsRepository;
            _searchService = searchService;
            _outboxWriter = outboxWriter;
            _registrationRepository = registrationRepository;
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
            bool isPrivate,
            int maxParticipants,
            int registerCost,
            EventCategory category,
            string? venueName,
            string? city,
            double? latitude,
            double? longitude,
            List<string>? tags)
        {
            try
            {
                var club = await _clubService.GetClub(clubId);

                if (club.UserId != userId)
                    throw new ForbiddenException("Not allowed");

                var requestedImageUrls = imageUrls.Take(5).ToList();
                await ValidateUploadedImageUrlsAsync(clubId, userId, requestedImageUrls);

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
                    ClubId = clubId,
                    Category = category,
                    VenueName = venueName,
                    City = city,
                    Latitude = latitude,
                    Longitude = longitude,
                    Tags = NormalizeTags(tags)
                };

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var created = await _eventsRepository.CreateAsync(ev);
                await _db.SaveChangesAsync();

                await _imageRepository.AddImagesAsync(created.Id, requestedImageUrls);
                _outboxWriter.StageUpsert(created);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var withImages = await _eventsRepository.GetByIdAsync(created.Id)
                    ?? throw new InternalServerErrorException("Failed to reload created event");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();

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

        public async Task<Events> GetVisibleEvent(int eventId, int? userId = null)
        {
            try
            {
                var ev = await GetEvent(eventId);
                if (await CanViewEventAsync(ev, userId))
                    return ev;

                throw new ResourceNotFoundException($"Event {eventId} not found");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetVisibleEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<(List<Events> Events, int TotalCount, Dictionary<int, double> DistanceKmById, string Source)> GetEvents(EventSearchCriteria criteria)
        {
            try
            {
                var normalized = string.IsNullOrWhiteSpace(criteria.Query)
                    ? null
                    : criteria.Query.Trim().ToLowerInvariant();

                var effective = criteria with { Query = normalized };

                // Prefer ES for full-text, tag, and geo search. If ES is unavailable, fall back to MySQL
                // for the subset of filters/sorts we can honor safely.
                try
                {
                    var result = await _searchService.SearchAsync(effective);
                    if (result.Hits.Count == 0)
                        return (new List<Events>(), result.TotalCount, new Dictionary<int, double>(), ResponseSource.Elasticsearch);

                    var ids = result.Hits.Select(h => h.Id).ToList();
                    var esEvents = await _eventsRepository.GetByIdsAsync(ids);

                    var ordered = ids
                        .Select(id => esEvents.FirstOrDefault(e => e.Id == id))
                        .Where(e => e != null)
                        .Cast<Events>()
                        .ToList();

                    var distanceMap = result.Hits
                        .Where(h => h.DistanceKm.HasValue)
                        .ToDictionary(h => h.Id, h => h.DistanceKm!.Value);

                    return (ordered, result.TotalCount, distanceMap, ResponseSource.Elasticsearch);
                }
                catch (ElasticsearchDisabledException ex)
                {
                    Logger.Info($"[EventsService] Elasticsearch disabled. Falling back to MySQL search. {ex.Message}");
                }
                catch (ElasticsearchUnavailableException ex)
                {
                    Logger.Warn(ex, "[EventsService] Elasticsearch temporarily unavailable. Falling back to MySQL search.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EventsService] Elasticsearch search failed with a non-fallback error: {ex}");
                    throw;
                }

                EnsureFallbackSearchIsSupported(effective);

                var (events, totalCount) = await _eventsRepository.SearchAsync(effective);
                var fallbackDistanceMap = BuildDistanceMap(events, effective);
                return (events, totalCount, fallbackDistanceMap, ResponseSource.Database);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<(List<Events> Events, int TotalCount, string Source)> GetEventsByClub(
            int clubId,
            EventStatus? status = null,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                await _clubService.GetClub(clubId);

                var (events, totalCount) = await _eventsRepository.SearchAsync(new EventSearchCriteria
                {
                    Query = null,
                    ClubId = clubId,
                    IsPrivate = false,
                    Status = status,
                    Page = page,
                    PageSize = pageSize
                });

                return (events, totalCount, ResponseSource.Database);
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
            int registerCost,
            EventCategory category,
            string? venueName,
            string? city,
            double? latitude,
            double? longitude,
            List<string>? tags)
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

                List<string>? oldUrls = null;
                List<string>? requestedImageUrls = null;
                List<string>? removedUrls = null;

                if (imageUrls != null)
                {
                    requestedImageUrls = imageUrls.Take(5).ToList();
                    var existingUrls = existing.Images
                        .Select(i => i.ImageUrl)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    await ValidateUploadedImageUrlsAsync(
                        club.Id,
                        userId,
                        requestedImageUrls,
                        eventId,
                        existingUrls);

                    oldUrls = existing.Images.Select(i => i.ImageUrl).ToList();
                    removedUrls = oldUrls
                        .Except(requestedImageUrls, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                await using var transaction = await _db.Database.BeginTransactionAsync();

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
                    ClubId = club.Id,
                    Category = category,
                    VenueName = venueName,
                    City = city,
                    Latitude = latitude,
                    Longitude = longitude,
                    Tags = NormalizeTags(tags)
                }) ?? throw new InternalServerErrorException("Update failed");

                if (requestedImageUrls != null)
                {
                    await _imageRepository.DeleteAllByEventIdAsync(eventId);
                    await _imageRepository.AddImagesAsync(eventId, requestedImageUrls);
                }

                _outboxWriter.StageUpsert(updated);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload updated event");

                if (removedUrls != null && removedUrls.Count > 0)
                    _ = Task.WhenAll(removedUrls.Select(u => _blobService.DeleteBlobAsync(u)));

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();

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

                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (!await _eventsRepository.DeleteAsync(eventId))
                    throw new InternalServerErrorException("Delete failed");

                _outboxWriter.StageDelete(eventId);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var urls = ev.Images.Select(i => i.ImageUrl).ToList();
                if (urls.Count > 0)
                    _ = Task.WhenAll(urls.Select(u => _blobService.DeleteBlobAsync(u)));

                await _cache.DeleteKeyAsync($"event:{eventId}");
                await BumpEventListVersionAsync();
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

        private static void EnsureFallbackSearchIsSupported(EventSearchCriteria criteria)
        {
            if (criteria.Tags != null && criteria.Tags.Count > 0)
            {
                throw new NotAvailableException(
                    "Tag filtering is temporarily unavailable because search indexing is unavailable.");
            }
        }

        private static Dictionary<int, double> BuildDistanceMap(
            IEnumerable<Events> events,
            EventSearchCriteria criteria)
        {
            if (!criteria.Lat.HasValue || !criteria.Lng.HasValue)
                return new Dictionary<int, double>();

            var map = new Dictionary<int, double>();
            foreach (var ev in events)
            {
                if (!ev.Latitude.HasValue || !ev.Longitude.HasValue)
                    continue;

                map[ev.Id] = CalculateDistanceKm(
                    criteria.Lat.Value,
                    criteria.Lng.Value,
                    ev.Latitude.Value,
                    ev.Longitude.Value);
            }

            return map;
        }

        private static double CalculateDistanceKm(
            double originLat,
            double originLng,
            double targetLat,
            double targetLng)
        {
            const double EarthRadiusKm = 6371.0;

            var dLat = DegreesToRadians(targetLat - originLat);
            var dLng = DegreesToRadians(targetLng - originLng);
            var originLatRad = DegreesToRadians(originLat);
            var targetLatRad = DegreesToRadians(targetLat);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(originLatRad) * Math.Cos(targetLatRad) *
                    Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusKm * c;
        }

        private static double DegreesToRadians(double degrees) =>
            degrees * (Math.PI / 180.0);

        public async Task<List<Events>> GetVisibleEventsByIds(IEnumerable<int> ids, int? userId = null)
        {
            try
            {
                var requestedIds = ids.Distinct().ToList();
                if (requestedIds.Count == 0)
                    return new List<Events>();

                var events = await _eventsRepository.GetByIdsAsync(requestedIds);
                var eventsById = events.ToDictionary(e => e.Id);
                var visible = new List<Events>(requestedIds.Count);

                foreach (var id in requestedIds)
                {
                    if (!eventsById.TryGetValue(id, out var ev))
                        continue;

                    if (await CanViewEventAsync(ev, userId))
                        visible.Add(ev);
                }

                return visible;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetVisibleEventsByIds failed: {e}");
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
                foreach (var item in itemList)
                    await ValidateUploadedImageUrlsAsync(clubId, userId, item.ImageUrls.Take(5).ToList());

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
                    ClubId = clubId,
                    Category = item.Category,
                    VenueName = item.VenueName,
                    City = item.City,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
                    Tags = NormalizeTags(item.Tags)
                }).ToList();

                await using var transaction = await _db.Database.BeginTransactionAsync();

                var created = await _eventsRepository.CreateManyAsync(entities);
                await _db.SaveChangesAsync();

                foreach (var (ev, item) in created.Zip(itemList))
                {
                    await _imageRepository.AddImagesAsync(ev.Id, item.ImageUrls.Take(5));
                    _outboxWriter.StageUpsert(ev);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var reloaded = await _eventsRepository.GetByIdsAsync(created.Select(e => e.Id));
                var byId = reloaded.ToDictionary(e => e.Id);
                var ordered = created.Select(e => byId[e.Id]).ToList();

                await BumpEventListVersionAsync();

                return new BatchCreateResultResponse
                {
                    Created = ordered.Select(e => EventMapper.MapToResponse(e)).ToList()
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

                EnsureNoDuplicateIds(ids);

                var requestedIds = ids.Distinct().ToList();

                var existing = await _eventsRepository.GetByIdsAsync(requestedIds);

                EnsureAllRequestedEventsExist(requestedIds, existing.Select(ev => ev.Id));

                if (existing.Any(ev => ev.ClubId != club.Id))
                    throw new ForbiddenException("One or more events do not belong to your club");

                await using var transaction = await _db.Database.BeginTransactionAsync();

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
                        if (item.Category.HasValue) ev.Category = item.Category.Value;
                        if (item.VenueName != null) ev.VenueName = item.VenueName;
                        if (item.City != null) ev.City = item.City;
                        if (item.Latitude.HasValue) ev.Latitude = item.Latitude;
                        if (item.Longitude.HasValue) ev.Longitude = item.Longitude;
                        if (item.Tags != null) ev.Tags = NormalizeTags(item.Tags);
                    })
                ));

                var count = await _eventsRepository.UpdateManyAsync(patches);
                var updatedEvents = await _db.Events
                    .Where(ev => requestedIds.Contains(ev.Id))
                    .ToListAsync();

                foreach (var ev in updatedEvents)
                    _outboxWriter.StageUpsert(ev);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await Task.WhenAll(requestedIds.Select(id => _cache.DeleteKeyAsync($"event:{id}")));
                await BumpEventListVersionAsync();

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

                EnsureNoDuplicateIds(idList);

                var requestedIds = idList.Distinct().ToList();

                var existing = await _eventsRepository.GetByIdsAsync(requestedIds);

                EnsureAllRequestedEventsExist(requestedIds, existing.Select(ev => ev.Id));

                if (existing.Any(ev => ev.ClubId != club.Id))
                    throw new ForbiddenException("One or more events do not belong to your club");

                var imageUrls = existing
                    .SelectMany(ev => ev.Images.Select(i => i.ImageUrl))
                    .ToList();

                await using var transaction = await _db.Database.BeginTransactionAsync();

                foreach (var id in requestedIds)
                    _outboxWriter.StageDelete(id);

                var deleted = await _eventsRepository.DeleteManyAsync(requestedIds);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                if (imageUrls.Count > 0)
                    _ = Task.WhenAll(imageUrls.Select(url => _blobService.DeleteBlobAsync(url)));
                await Task.WhenAll(requestedIds.Select(id => _cache.DeleteKeyAsync($"event:{id}")));
                await BumpEventListVersionAsync();

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
            int clubId,
            int userId,
            string fileName,
            string contentType,
            int? eventId = null)
        {
            try
            {
                var club = await _clubService.GetClubByUser(userId);
                if (club.Id != clubId)
                    throw new ForbiddenException("Not allowed");

                if (eventId.HasValue)
                {
                    var ev = await GetEvent(eventId.Value);
                    if (ev.ClubId != clubId)
                        throw new ForbiddenException("Not allowed");
                }

                var scope = eventId.HasValue
                    ? $"events/clubs/{clubId}/events/{eventId.Value}"
                    : $"events/clubs/{clubId}/pending";

                var result = await _blobService.GenerateUploadUrlAsync(scope, fileName, contentType);

                var intent = new EventImageUploadIntent(
                    clubId,
                    eventId,
                    userId,
                    result.PublicUrl,
                    contentType.Trim().ToLowerInvariant()
                );

                var stored = await _cache.SetValueAsync(
                    GetImageUploadIntentKey(result.PublicUrl),
                    JsonSerializer.Serialize(intent),
                    ImageUploadIntentTTL
                );

                if (!stored)
                {
                    throw new NotAvailableException(
                        "Image uploads are temporarily unavailable. Please try again shortly.");
                }

                return result;
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

                await ValidateUploadedImageUrlsAsync(club.Id, userId, new[] { imageUrl }, eventId);

                var count = await _imageRepository.CountByEventIdAsync(eventId);
                if (count >= 5)
                    throw new BadRequestException("An event cannot have more than 5 images.");

                var images = await _imageRepository.AddImagesAsync(eventId, new[] { imageUrl });
                await _db.SaveChangesAsync();

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
                await _db.SaveChangesAsync();

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

        private static List<string> NormalizeTags(IEnumerable<string>? tags)
        {
            if (tags == null) return new List<string>();
            return tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .Take(10)
                .ToList();
        }

        private static void EnsureNoDuplicateIds(IEnumerable<int> ids)
        {
            var duplicateIds = ids
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateIds.Count > 0)
            {
                throw new BadRequestException(
                    $"Duplicate event IDs are not allowed: {string.Join(", ", duplicateIds)}.");
            }
        }

        private static void EnsureAllRequestedEventsExist(
            IEnumerable<int> requestedIds,
            IEnumerable<int> foundIds)
        {
            var foundIdSet = foundIds.ToHashSet();
            var missingIds = requestedIds
                .Where(id => !foundIdSet.Contains(id))
                .ToList();

            if (missingIds.Count > 0)
            {
                throw new ResourceNotFoundException(
                    $"One or more requested events were not found: {string.Join(", ", missingIds)}");
            }
        }

        private async Task ValidateUploadedImageUrlsAsync(
            int clubId,
            int userId,
            IEnumerable<string> imageUrls,
            int? eventId = null,
            ISet<string>? existingUrls = null)
        {
            var urls = imageUrls.ToList();
            foreach (var imageUrl in urls)
            {
                if (existingUrls?.Contains(imageUrl) == true)
                    continue;

                await ValidateNewUploadedImageUrlAsync(clubId, userId, imageUrl, eventId);
            }
        }

        private async Task ValidateNewUploadedImageUrlAsync(
            int clubId,
            int userId,
            string imageUrl,
            int? eventId = null)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Event images must use a valid HTTPS URL.");
            }

            if (!_blobService.IsOwnedBlobUrl(imageUrl))
            {
                throw new BadRequestException(
                    "Event images must reference uploads issued by this service.");
            }

            var intentPayload = await _cache.GetValueAsync(GetImageUploadIntentKey(imageUrl));
            if (intentPayload == null)
            {
                throw new BadRequestException(
                    "Image upload is invalid or expired. Please upload the image again.");
            }

            var intent = JsonSerializer.Deserialize<EventImageUploadIntent>(intentPayload);
            if (intent == null ||
                intent.UserId != userId ||
                intent.ClubId != clubId ||
                !string.Equals(intent.PublicUrl, imageUrl, StringComparison.Ordinal))
            {
                throw new BadRequestException(
                    "Image upload is invalid or does not belong to this organizer.");
            }

            if (intent.EventId.HasValue && intent.EventId != eventId)
            {
                throw new BadRequestException(
                    "Image upload does not belong to the specified event.");
            }
        }

        private static string GetImageUploadIntentKey(string imageUrl)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(imageUrl));
            return $"event:image-upload:intent:{Convert.ToHexString(bytes)}";
        }

        private async Task<bool> CanViewEventAsync(Events ev, int? userId)
        {
            if (!ev.isPrivate)
                return true;

            if (!userId.HasValue)
                return false;

            var club = await _clubService.GetClub(ev.ClubId);
            if (club.UserId == userId.Value)
                return true;

            var registration = await _registrationRepository.IsRegisteredAsync(ev.Id, userId.Value);
            return registration != null;
        }

        public async Task NotifyRegistrationChangedAsync(int eventId)
        {
            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                if (ev == null)
                    return;

                _outboxWriter.StageUpsert(ev);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                await _cache.DeleteKeyAsync($"event:{eventId}");
            }
            catch (Exception e)
            {
                Logger.Warn(e, $"[EventsService] NotifyRegistrationChangedAsync failed for event {eventId}");
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



