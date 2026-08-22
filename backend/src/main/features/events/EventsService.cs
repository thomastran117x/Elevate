using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events.access;
using backend.main.features.events.analytics;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.events.waitlist;
using backend.main.features.payment;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;


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
        private readonly IRefreshAheadCache _refreshCache;
        private readonly IEventAnalyticsRepository _analyticsRepository;
        private readonly IEventSearchService _searchService;
        private readonly IEventSearchOutboxWriter _outboxWriter;
        private readonly IEventAccessChecker _accessChecker;
        private readonly IEventWaitlistPromoter _waitlistPromoter;
        private readonly EventVersioningOptions _versioningOptions;
        private readonly TimeProvider _timeProvider;

        private static readonly TimeSpan EventTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EventListTTL = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);
        private static readonly JsonSerializerOptions EventCacheSerializerOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };
        private const string NullSentinel = "__null__";

        public EventsService(
            AppDatabaseContext db,
            IEventsRepository eventsRepository,
            IEventImageRepository imageRepository,
            IClubService clubService,
            IAzureBlobService blobService,
            ICacheService cache,
            IRefreshAheadCache refreshCache,
            IEventAnalyticsRepository analyticsRepository,
            IEventSearchService searchService,
            IEventSearchOutboxWriter outboxWriter,
            IEventAccessChecker accessChecker,
            IEventWaitlistPromoter waitlistPromoter,
            IOptions<EventVersioningOptions> versioningOptions,
            TimeProvider timeProvider)
        {
            _db = db;
            _eventsRepository = eventsRepository;
            _imageRepository = imageRepository;
            _clubService = clubService;
            _blobService = blobService;
            _cache = cache;
            _refreshCache = refreshCache;
            _analyticsRepository = analyticsRepository;
            _searchService = searchService;
            _outboxWriter = outboxWriter;
            _accessChecker = accessChecker;
            _waitlistPromoter = waitlistPromoter;
            _versioningOptions = versioningOptions.Value;
            _timeProvider = timeProvider;
        }

        public async Task<Events> CreateEvent(
            int clubId,
            int userId,
            string userRole,
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
                await EnsureCanManageClubAsync(clubId, userId, userRole);

                var requestedImageUrls = imageUrls.Take(5).ToList();
                await ValidateUploadedImageUrlsAsync(clubId, userId, requestedImageUrls);

                var now = GetUtcNow();

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it. The entity
                // is built inside the delegate because a retry re-runs it, and reusing an
                // instance from a previous attempt would re-Add one the change tracker already
                // knows, inserting a duplicate row and staging the outbox entry twice.
                var strategy = _db.Database.CreateExecutionStrategy();
                var created = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    var draft = await _eventsRepository.CreateAsync(new Events
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
                        LifecycleState = EventLifecycleState.Draft,
                        Category = category,
                        VenueName = venueName,
                        City = city,
                        Latitude = latitude,
                        Longitude = longitude,
                        Tags = NormalizeTags(tags),
                        CurrentVersionNumber = 1,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    await _db.SaveChangesAsync();

                    AddVersionRecord(
                        draft,
                        EventVersionActions.Create,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: BuildChangedFields(null, BuildSnapshot(draft)),
                        createdAt: now);

                    await _imageRepository.AddImagesAsync(draft.Id, requestedImageUrls);
                    _outboxWriter.StageSync(draft);

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return draft;
                });

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
            var ev = await _refreshCache.GetOrSetAsync(
                EventCacheKeys.Event(eventId),
                () => _eventsRepository.GetByIdAsync(eventId),
                EventTTL,
                nullSentinelTtl: NotFoundTTL,
                serializerOptions: EventCacheSerializerOptions);

            return ev ?? throw new ResourceNotFoundException($"Event {eventId} not found");
        }

        public async Task<Events> GetVisibleEvent(int eventId, int? userId = null, string? userRole = null)
        {
            try
            {
                var ev = await GetEvent(eventId);
                if (await CanViewEventAsync(ev, userId, userRole))
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

        public async Task EnsureCanViewEventAsync(int eventId, int userId, string userRole)
        {
            var ev = await GetEvent(eventId);
            if (await CanViewEventAsync(ev, userId, userRole))
                return;

            throw new ResourceNotFoundException($"Event {eventId} not found");
        }

        public async Task<(List<Events> Events, int TotalCount, Dictionary<int, double> DistanceKmById, string Source)> GetEvents(EventSearchCriteria criteria)
        {
            try
            {
                var normalized = string.IsNullOrWhiteSpace(criteria.Query)
                    ? null
                    : criteria.Query.Trim().ToLowerInvariant();

                var effective = criteria with
                {
                    Query = normalized
                };

                if (!EventSearchFallbackSupport.RequiresDatabaseFallback(effective))
                {
                    // Prefer ES for full-text, tag, and geo search. If ES is unavailable, fall back to database
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
                        Logger.Info($"[EventsService] Elasticsearch disabled. Falling back to database search. {ex.Message}");
                    }
                    catch (ElasticsearchUnavailableException ex)
                    {
                        Logger.Warn(ex, "[EventsService] Elasticsearch temporarily unavailable. Falling back to database search.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[EventsService] Elasticsearch search failed with a non-fallback error: {ex}");
                        throw;
                    }
                }
                else
                {
                    Logger.Info("[EventsService] Falling back to database search for a query that requires literal wildcard matching.");
                }
                EventSearchFallbackSupport.EnsureSupported(effective);
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
            int pageSize = 20,
            string? search = null)
        {
            try
            {
                await _clubService.GetClub(clubId);

                var (events, totalCount) = await _eventsRepository.SearchAsync(new EventSearchCriteria
                {
                    Query = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    ClubId = clubId,
                    IsPrivate = false,
                    LifecycleState = EventLifecycleState.Published,
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

        public async Task<(List<Events> Events, int TotalCount)> GetManageableEventsByClub(
            int clubId,
            int userId,
            string userRole,
            EventLifecycleState? lifecycleState = null,
            int page = 1,
            int pageSize = 20,
            string? search = null)
        {
            try
            {
                await EnsureCanManageClubAsync(clubId, userId, userRole);

                var criteria = new EventSearchCriteria
                {
                    Query = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    ClubId = clubId,
                    LifecycleState = lifecycleState,
                    Page = NormalizePage(page),
                    PageSize = NormalizePageSize(pageSize),
                    SortBy = EventSortBy.Date
                };

                var (events, totalCount) = await _eventsRepository.SearchAsync(criteria);
                return (events, totalCount);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetManageableEventsByClub failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Events> GetManageableEvent(int eventId, int userId, string userRole)
        {
            try
            {
                var ev = await GetEvent(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);
                return ev;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetManageableEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Events> CreateDraftEvent(int clubId, int userId, string userRole, EventDraftUpsertRequest request)
        {
            try
            {
                await EnsureCanManageClubAsync(clubId, userId, userRole);

                var requestedImageUrls = request.ImageUrls?.Take(5).ToList() ?? [];
                await ValidateUploadedImageUrlsAsync(clubId, userId, requestedImageUrls);

                var now = GetUtcNow();
                var ev = new Events
                {
                    Name = request.Name?.Trim(),
                    Description = request.Description?.Trim(),
                    Location = request.Location?.Trim(),
                    isPrivate = request.IsPrivate ?? false,
                    maxParticipants = request.MaxParticipants ?? 0,
                    registerCost = request.RegisterCost ?? 0,
                    WaitlistEnabled = request.WaitlistEnabled ?? false,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    ClubId = clubId,
                    LifecycleState = EventLifecycleState.Draft,
                    Category = request.Category ?? EventCategory.Other,
                    VenueName = request.VenueName?.Trim(),
                    City = request.City?.Trim(),
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    Tags = NormalizeTags(request.Tags),
                    CurrentVersionNumber = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                var created = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    var draft = await _eventsRepository.CreateAsync(ev);
                    await _db.SaveChangesAsync();

                    AddVersionRecord(
                        draft,
                        EventVersionActions.Create,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: BuildChangedFields(null, BuildSnapshot(draft)),
                        createdAt: now);

                    if (requestedImageUrls.Count > 0)
                        await _imageRepository.AddImagesAsync(draft.Id, requestedImageUrls);

                    _outboxWriter.StageSync(draft);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return draft;
                });

                var withImages = await _eventsRepository.GetByIdAsync(created.Id)
                    ?? throw new InternalServerErrorException("Failed to reload created draft event");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] CreateDraftEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Events> UpdateDraftEvent(int eventId, int userId, string userRole, EventDraftUpsertRequest request)
        {
            try
            {
                var existing = await GetTrackedEventOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(existing, userId, userRole);

                var previousSnapshot = BuildSnapshot(existing);
                var previousMax = existing.maxParticipants;
                var previousWaitlistEnabled = existing.WaitlistEnabled;
                var now = GetUtcNow();

                List<string>? oldUrls = null;
                List<string>? removedUrls = null;
                var requestedImageUrls = request.ImageUrls?.Take(5).ToList();

                if (requestedImageUrls != null)
                {
                    var existingUrls = existing.Images
                        .Select(i => i.ImageUrl)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    await ValidateUploadedImageUrlsAsync(
                        existing.ClubId,
                        userId,
                        requestedImageUrls,
                        eventId,
                        existingUrls);

                    oldUrls = existing.Images.Select(i => i.ImageUrl).ToList();
                    removedUrls = oldUrls
                        .Except(requestedImageUrls, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (request.Name != null)
                    existing.Name = request.Name.Trim();
                if (request.Description != null)
                    existing.Description = request.Description.Trim();
                if (request.Location != null)
                    existing.Location = request.Location.Trim();
                if (request.IsPrivate.HasValue)
                    existing.isPrivate = request.IsPrivate.Value;
                if (request.MaxParticipants.HasValue)
                    existing.maxParticipants = request.MaxParticipants.Value;
                if (request.RegisterCost.HasValue)
                    existing.registerCost = request.RegisterCost.Value;
                if (request.WaitlistEnabled.HasValue)
                    existing.WaitlistEnabled = request.WaitlistEnabled.Value;

                // Validate the MERGED state, not just the field pairs present in the request.
                // This is a partial PATCH, so `{ waitlistEnabled: true }` alone against an
                // unlimited event — or `{ maxParticipants: 0 }` alone against an event whose
                // waitlist is already on — clears the request-level checks while persisting a
                // combination that JoinAsync would reject outright.
                if (existing.WaitlistEnabled && existing.maxParticipants <= 0)
                    throw new BadRequestException("Waitlists require a capacity limit.");

                if (existing.WaitlistEnabled && existing.registerCost > 0)
                    throw new BadRequestException("Waitlists are not available for paid events.");

                if (request.StartTime.HasValue)
                    existing.StartTime = request.StartTime.Value;
                if (request.EndTime.HasValue || request.StartTime.HasValue)
                    existing.EndTime = request.EndTime;
                if (request.Category.HasValue)
                    existing.Category = request.Category.Value;
                if (request.VenueName != null)
                    existing.VenueName = request.VenueName.Trim();
                if (request.City != null)
                    existing.City = request.City.Trim();
                if (request.Latitude.HasValue || request.Longitude.HasValue)
                {
                    existing.Latitude = request.Latitude;
                    existing.Longitude = request.Longitude;
                }
                if (request.Tags != null)
                    existing.Tags = NormalizeTags(request.Tags);

                MarkSeriesOverridden(existing);

                existing.CurrentVersionNumber += 1;
                existing.UpdatedAt = now;

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    if (requestedImageUrls != null)
                    {
                        await _imageRepository.DeleteAllByEventIdAsync(eventId);
                        if (requestedImageUrls.Count > 0)
                            await _imageRepository.AddImagesAsync(eventId, requestedImageUrls);
                    }

                    AddVersionRecord(
                        existing,
                        EventVersionActions.Update,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: BuildChangedFields(previousSnapshot, BuildSnapshot(existing)),
                        createdAt: now);

                    _outboxWriter.StageSync(existing);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload updated draft event");

                if (removedUrls != null && removedUrls.Count > 0)
                    _ = Task.WhenAll(removedUrls.Select(u => _blobService.DeleteBlobAsync(u)));

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();
                await TryPromoteWaitlistAsync(eventId, previousMax, previousWaitlistEnabled, withImages);

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] UpdateDraftEvent failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public Task<Events> PublishEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Published, EventVersionActions.Publish);

        public Task<Events> CancelEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Cancelled, EventVersionActions.Cancel);

        public Task<Events> ArchiveEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Archived, EventVersionActions.Archive);

        public Task<Events> PauseEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Paused, EventVersionActions.Pause);

        public Task<Events> ResumeEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Published, EventVersionActions.Resume);

        public Task<Events> ReinstateEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Published, EventVersionActions.Reinstate);

        public Task<Events> UnarchiveEvent(int eventId, int userId, string userRole) =>
            TransitionLifecycleAsync(eventId, userId, userRole, EventLifecycleState.Paused, EventVersionActions.Unarchive);

        public async Task<Events> UpdateEvent(
            int eventId,
            int userId,
            string userRole,
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
                var existing = await GetTrackedEventOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(existing, userId, userRole);

                // Paused events stay editable on purpose — reworking one while it is off sale is
                // the main reason to pause it.
                if (!EventLifecyclePolicy.AllowsEditing(existing.LifecycleState))
                    throw new ConflictException(
                        $"Cannot update a {existing.LifecycleState.ToString().ToLower()} event.");

                if (maxParticipants > 0 && maxParticipants < existing.RegistrationCount)
                    throw new BadRequestException(
                        $"maxParticipants ({maxParticipants}) cannot be less than the current registration count ({existing.RegistrationCount}).");

                var previousSnapshot = BuildSnapshot(existing);
                var previousMax = existing.maxParticipants;
                var previousWaitlistEnabled = existing.WaitlistEnabled;

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
                        existing.ClubId,
                        userId,
                        requestedImageUrls,
                        eventId,
                        existingUrls);

                    oldUrls = existing.Images.Select(i => i.ImageUrl).ToList();
                    removedUrls = oldUrls
                        .Except(requestedImageUrls, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                existing.Name = name;
                existing.Description = description;
                existing.Location = location;
                existing.StartTime = startTime;
                existing.EndTime = endTime;
                existing.isPrivate = isPrivate;
                existing.maxParticipants = maxParticipants;
                existing.registerCost = registerCost;
                existing.Category = category;
                existing.VenueName = venueName;
                existing.City = city;
                existing.Latitude = latitude;
                existing.Longitude = longitude;
                existing.Tags = NormalizeTags(tags);

                MarkSeriesOverridden(existing);

                existing.CurrentVersionNumber += 1;
                existing.UpdatedAt = GetUtcNow();

                var changedFields = BuildChangedFields(previousSnapshot, BuildSnapshot(existing));

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    if (requestedImageUrls != null)
                    {
                        await _imageRepository.DeleteAllByEventIdAsync(eventId);
                        await _imageRepository.AddImagesAsync(eventId, requestedImageUrls);
                    }

                    AddVersionRecord(
                        existing,
                        EventVersionActions.Update,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: changedFields,
                        createdAt: existing.UpdatedAt);

                    _outboxWriter.StageSync(existing);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload updated event");

                if (removedUrls != null && removedUrls.Count > 0)
                    _ = Task.WhenAll(removedUrls.Select(u => _blobService.DeleteBlobAsync(u)));

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();
                await TryPromoteWaitlistAsync(eventId, previousMax, previousWaitlistEnabled, withImages);

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

        public async Task DeleteEvent(int eventId, int userId, string userRole)
        {
            try
            {
                var ev = await GetEvent(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                // Deletion is unrecoverable — it drops the row, its version history, and its
                // blobs. Everything short of a never-published draft or an already-archived
                // event must go through archiving first, which is reversible.
                if (!EventLifecyclePolicy.AllowsHardDelete(ev.LifecycleState))
                {
                    throw new ConflictException(
                        $"A {ev.LifecycleState.ToString().ToLowerInvariant()} event cannot be deleted. " +
                        "Archive it first — archiving is reversible, deleting is not.");
                }

                // Mirrors EventSeriesService.DeleteAsync, which refuses to delete an occurrence
                // that people have signed up to.
                if (ev.RegistrationCount > 0)
                {
                    throw new ConflictException(
                        $"This event has {ev.RegistrationCount} registration(s) and cannot be deleted. " +
                        "Archiving keeps them on record.");
                }

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    if (!await _eventsRepository.DeleteAsync(eventId))
                        throw new InternalServerErrorException("Delete failed");

                    _outboxWriter.StageDelete(eventId);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                var urls = ev.Images.Select(i => i.ImageUrl).ToList();
                if (urls.Count > 0)
                    _ = Task.WhenAll(urls.Select(u => _blobService.DeleteBlobAsync(u)));

                await _refreshCache.RemoveAsync(EventCacheKeys.Event(eventId));
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

        public async Task<List<Events>> GetVisibleEventsByIds(IEnumerable<int> ids, int? userId = null, string? userRole = null)
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

                    if (await CanViewEventAsync(ev, userId, userRole))
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
            string userRole,
            IEnumerable<BatchCreateEventItem> items)
        {
            try
            {
                await EnsureCanManageClubAsync(clubId, userId, userRole);

                var itemList = items.ToList();
                foreach (var item in itemList)
                    await ValidateUploadedImageUrlsAsync(clubId, userId, item.ImageUrls.Take(5).ToList());

                var now = GetUtcNow();
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
                    LifecycleState = EventLifecycleState.Published,
                    Category = item.Category,
                    VenueName = item.VenueName,
                    City = item.City,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
                    Tags = NormalizeTags(item.Tags),
                    CurrentVersionNumber = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList();

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                var created = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    var createdEntities = await _eventsRepository.CreateManyAsync(entities);
                    await _db.SaveChangesAsync();

                    foreach (var (ev, item) in createdEntities.Zip(itemList))
                    {
                        AddVersionRecord(
                            ev,
                            EventVersionActions.Create,
                            actorUserId: userId,
                            actorRole: NormalizeActorRole(userRole),
                            rollbackSourceVersionNumber: null,
                            changedFields: BuildChangedFields(null, BuildSnapshot(ev)),
                            createdAt: now);
                        await _imageRepository.AddImagesAsync(ev.Id, item.ImageUrls.Take(5));
                        _outboxWriter.StageSync(ev);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return createdEntities;
                });

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

        public async Task<int> BatchUpdateEvents(int userId, string userRole, IEnumerable<BatchUpdateEventItem> items)
        {
            try
            {
                var itemList = items.ToList();
                var ids = itemList.Select(i => i.EventId).ToList();

                EnsureNoDuplicateIds(ids);

                var requestedIds = ids.Distinct().ToList();

                var existing = await _eventsRepository.GetByIdsAsync(requestedIds);

                EnsureAllRequestedEventsExist(requestedIds, existing.Select(ev => ev.Id));
                await EnsureCanManageAllClubIdsAsync(
                    existing.Select(ev => ev.ClubId).Distinct(),
                    userId,
                    userRole,
                    "One or more events do not belong to a club you can manage");

                var trackedEvents = await _db.Events
                    .Include(ev => ev.Images)
                    .Where(ev => requestedIds.Contains(ev.Id))
                    .ToDictionaryAsync(ev => ev.Id);

                var now = GetUtcNow();

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    foreach (var item in itemList)
                    {
                        var ev = trackedEvents[item.EventId];
                        if (ev.LifecycleState == EventLifecycleState.Draft)
                            throw new BadRequestException($"Event {ev.Id} must be updated through the draft workflow.");

                        var previousSnapshot = BuildSnapshot(ev);

                        ApplyBatchPatch(ev, item);

                        // Batch update is another way to edit a single event, so an occurrence
                        // changed here has to be flagged like any other one-off edit. Without this
                        // a later "update this and following occurrences" would silently overwrite
                        // the change, unlike the same edit made through the single-event endpoints.
                        MarkSeriesOverridden(ev);

                        ev.CurrentVersionNumber += 1;
                        ev.UpdatedAt = now;

                        var publishIssues = EventLifecyclePolicy.GetPublishIssues(ev, now);
                        if (publishIssues.Count > 0)
                        {
                            throw new BadRequestException(
                                $"Event {ev.Id} is not publish-ready: {string.Join(" ", publishIssues)}");
                        }

                        AddVersionRecord(
                            ev,
                            EventVersionActions.Update,
                            actorUserId: userId,
                            actorRole: NormalizeActorRole(userRole),
                            rollbackSourceVersionNumber: null,
                            changedFields: BuildChangedFields(previousSnapshot, BuildSnapshot(ev)),
                            createdAt: now);

                        _outboxWriter.StageSync(ev);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                await Task.WhenAll(requestedIds.Select(id => _refreshCache.RemoveAsync(EventCacheKeys.Event(id))));
                await BumpEventListVersionAsync();

                return trackedEvents.Count;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] BatchUpdateEvents failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<int> BatchDeleteEvents(int userId, string userRole, IEnumerable<int> ids)
        {
            try
            {
                var idList = ids.ToList();

                EnsureNoDuplicateIds(idList);

                var requestedIds = idList.Distinct().ToList();

                var existing = await _eventsRepository.GetByIdsAsync(requestedIds);

                EnsureAllRequestedEventsExist(requestedIds, existing.Select(ev => ev.Id));
                await EnsureCanManageAllClubIdsAsync(
                    existing.Select(ev => ev.ClubId).Distinct(),
                    userId,
                    userRole,
                    "One or more events do not belong to a club you can manage");

                // Same guard as the single delete — otherwise the batch endpoint is a way
                // around it, and deleting is the one thing nothing can walk back.
                var undeletable = existing
                    .Where(ev => !EventLifecyclePolicy.AllowsHardDelete(ev.LifecycleState))
                    .Select(ev => ev.Id)
                    .ToList();

                if (undeletable.Count > 0)
                {
                    throw new ConflictException(
                        $"Events {string.Join(", ", undeletable)} must be archived before they can be deleted. " +
                        "Archiving is reversible, deleting is not.");
                }

                var withRegistrations = existing
                    .Where(ev => ev.RegistrationCount > 0)
                    .Select(ev => ev.Id)
                    .ToList();

                if (withRegistrations.Count > 0)
                {
                    throw new ConflictException(
                        $"Events {string.Join(", ", withRegistrations)} have registrations and cannot be deleted. " +
                        "Archiving keeps them on record.");
                }

                var imageUrls = existing
                    .SelectMany(ev => ev.Images.Select(i => i.ImageUrl))
                    .ToList();

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                var deleted = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    foreach (var id in requestedIds)
                        _outboxWriter.StageDelete(id);

                    var deletedCount = await _eventsRepository.DeleteManyAsync(requestedIds);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return deletedCount;
                });

                if (imageUrls.Count > 0)
                    _ = Task.WhenAll(imageUrls.Select(url => _blobService.DeleteBlobAsync(url)));
                await Task.WhenAll(requestedIds.Select(id => _refreshCache.RemoveAsync(EventCacheKeys.Event(id))));
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

        public async Task<(List<EventVersionHistoryItem> Items, int TotalCount)> GetVersionHistoryAsync(
            int eventId,
            int userId,
            string userRole,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                page = NormalizePage(page);
                pageSize = NormalizePageSize(pageSize);

                var ev = await GetEventRecordOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                var query = _db.EventVersions
                    .AsNoTracking()
                    .Where(v => v.EventId == eventId);

                var totalCount = await query.CountAsync();
                var versions = await query
                    .OrderByDescending(v => v.VersionNumber)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (
                    versions.Select(v => MapHistoryItem(v, ev.CurrentVersionNumber)).ToList(),
                    totalCount
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetVersionHistoryAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<EventVersionDetail> GetVersionDetailAsync(
            int eventId,
            int versionNumber,
            int userId,
            string userRole)
        {
            try
            {
                var ev = await GetEventRecordOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                var version = await _db.EventVersions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.EventId == eventId && v.VersionNumber == versionNumber)
                    ?? throw new ResourceNotFoundException(
                        $"Version {versionNumber} for event {eventId} was not found.");

                return new EventVersionDetail(
                    version.EventId,
                    version.VersionNumber,
                    version.ActionType,
                    version.CreatedAt,
                    version.ActorUserId,
                    version.ActorRole,
                    IsRollbackEligible(version, ev.CurrentVersionNumber),
                    GetRollbackExpiry(version.CreatedAt),
                    version.RollbackSourceVersionNumber,
                    DeserializeChangedFields(version.ChangedFieldsJson),
                    DeserializeSnapshot(version.SnapshotJson));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] GetVersionDetailAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<EventRollbackResult> RollbackToVersionAsync(
            int eventId,
            int versionNumber,
            int userId,
            string userRole)
        {
            try
            {
                var ev = await GetTrackedEventOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                var targetVersion = await _db.EventVersions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.EventId == eventId && v.VersionNumber == versionNumber)
                    ?? throw new ResourceNotFoundException(
                        $"Version {versionNumber} for event {eventId} was not found.");

                if (versionNumber == ev.CurrentVersionNumber)
                    throw new BadRequestException("Cannot roll back to the current version.");

                if (!IsRollbackEligible(targetVersion, ev.CurrentVersionNumber))
                    throw new BadRequestException("This version is no longer eligible for rollback.");

                var currentSnapshot = BuildSnapshot(ev);
                var targetSnapshot = DeserializeSnapshot(targetVersion.SnapshotJson);
                // A rollback can restore a higher capacity, which frees seats.
                var previousMax = ev.maxParticipants;
                var previousWaitlistEnabled = ev.WaitlistEnabled;

                // Preserve the current lifecycle state Ã¢â‚¬â€ rollback restores field values
                // only, not the event's published/cancelled state. Changing lifecycle
                // state must go through the explicit transition endpoints.
                var preservedLifecycleState = ev.LifecycleState;
                ApplySnapshot(ev, targetSnapshot);
                ev.LifecycleState = preservedLifecycleState;
                ev.CurrentVersionNumber += 1;
                ev.UpdatedAt = GetUtcNow();

                var changedFields = BuildChangedFields(currentSnapshot, BuildSnapshot(ev));

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    AddVersionRecord(
                        ev,
                        EventVersionActions.Rollback,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: targetVersion.VersionNumber,
                        changedFields: changedFields,
                        createdAt: ev.UpdatedAt);

                    _outboxWriter.StageSync(ev);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                await CacheEventAsync(ev);
                await BumpEventListVersionAsync();
                await TryPromoteWaitlistAsync(eventId, previousMax, previousWaitlistEnabled, ev);

                return new EventRollbackResult(ev, targetVersion.VersionNumber, ev.CurrentVersionNumber);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] RollbackToVersionAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<PresignedUploadResponse> GenerateImageUploadUrlAsync(
            int clubId,
            int userId,
            string userRole,
            string fileName,
            string contentType,
            int? eventId = null)
        {
            try
            {
                // clubId <= 0 (with no eventId) means the club does not exist yet — this is a
                // club-creation upload, so any authenticated user may request it and the
                // club-manage check is skipped. The issued URL is still an owned-blob URL.
                var isNewClubUpload = !eventId.HasValue && clubId <= 0;

                if (eventId.HasValue)
                {
                    var ev = await GetEvent(eventId.Value);
                    await EnsureCanManageEventMediaAsync(ev, userId, userRole);
                    if (ev.ClubId != clubId)
                        throw new ForbiddenException("Not allowed");
                }
                else if (!isNewClubUpload)
                {
                    await EnsureCanManageClubAsync(clubId, userId, userRole);
                }

                var scope = eventId.HasValue
                    ? $"events/clubs/{clubId}/events/{eventId.Value}"
                    : isNewClubUpload
                        ? $"clubs/pending/{userId}"
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
                    EventImageUploadValidator.IntentTtl
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

        public async Task<EventImage> AddEventImageAsync(int eventId, int userId, string userRole, string imageUrl)
        {
            try
            {
                var ev = await GetEvent(eventId);
                await EnsureCanManageEventMediaAsync(ev, userId, userRole);

                await ValidateUploadedImageUrlsAsync(ev.ClubId, userId, new[] { imageUrl }, eventId);

                var count = await _imageRepository.CountByEventIdAsync(eventId);
                if (count >= 5)
                    throw new BadRequestException("An event cannot have more than 5 images.");

                var images = await _imageRepository.AddImagesAsync(eventId, new[] { imageUrl });
                await _db.SaveChangesAsync();

                await _refreshCache.RemoveAsync(EventCacheKeys.Event(eventId));
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

        public async Task RemoveEventImageAsync(int eventId, int imageId, int userId, string userRole)
        {
            try
            {
                var ev = await GetEvent(eventId);
                await EnsureCanManageEventMediaAsync(ev, userId, userRole);

                var image = await _imageRepository.GetByIdAsync(imageId, eventId)
                    ?? throw new ResourceNotFoundException(
                        $"Image {imageId} not found on event {eventId}");

                await _imageRepository.DeleteImageAsync(imageId, eventId);
                await _db.SaveChangesAsync();

                _ = _blobService.DeleteBlobAsync(image.ImageUrl);

                await _refreshCache.RemoveAsync(EventCacheKeys.Event(eventId));
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

        public async Task<EventAnalyticsResponse> GetEventAnalytics(int eventId, int userId, string userRole)
        {
            try
            {
                var ev = await GetEvent(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                var data = await _analyticsRepository.GetEventAnalyticsAsync(eventId);

                var fillRate = ev.maxParticipants > 0
                    ? Math.Round(data.RegistrationCount / (double)ev.maxParticipants * 100.0, 2)
                    : 0.0;

                return new EventAnalyticsResponse
                {
                    EventId = ev.Id,
                    EventName = ev.Name ?? string.Empty,
                    LifecycleState = ev.LifecycleState,
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

        public async Task<ClubAnalyticsResponse> GetClubAnalytics(int clubId, int userId, string userRole)
        {
            try
            {
                await EnsureCanManageClubAsync(clubId, userId, userRole);

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
                    DraftEvents = data.DraftEvents,
                    PublishedEvents = data.PublishedEvents,
                    PausedEvents = data.PausedEvents,
                    CancelledEvents = data.CancelledEvents,
                    ArchivedEvents = data.ArchivedEvents,
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

        private async Task<Events> TransitionLifecycleAsync(
            int eventId,
            int userId,
            string userRole,
            EventLifecycleState targetState,
            string versionAction)
        {
            try
            {
                var ev = await GetTrackedEventOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                if (!EventLifecyclePolicy.CanTransition(ev.LifecycleState, targetState))
                {
                    throw new BadRequestException(
                        $"Cannot transition an event from {ev.LifecycleState} to {targetState}.");
                }

                var now = GetUtcNow();
                var previousSnapshot = BuildSnapshot(ev);

                if (targetState == EventLifecycleState.Published)
                {
                    var publishIssues = EventLifecyclePolicy.GetPublishIssues(ev, now);
                    if (publishIssues.Count > 0)
                    {
                        throw new BadRequestException(
                            $"This event is not ready to publish. {string.Join(" ", publishIssues)}");
                    }
                }

                // Records the previous state so the organizer can undo a misclick in one step;
                // the revert window is measured from LifecycleChangedAt.
                EventLifecyclePolicy.ApplyTransition(ev, targetState, now);
                ev.CurrentVersionNumber += 1;
                ev.UpdatedAt = now;

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    AddVersionRecord(
                        ev,
                        versionAction,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: BuildChangedFields(previousSnapshot, BuildSnapshot(ev)),
                        createdAt: now);

                    _outboxWriter.StageSync(ev);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload lifecycle transition result.");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();

                // Seats can free up while an event is off sale, but the promoter refuses to act
                // on anything that is not published, so reopening has to drain the queue.
                if (targetState == EventLifecycleState.Published && withImages.WaitlistEnabled)
                    await DrainWaitlistAsync(eventId);

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] TransitionLifecycleAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        /// <summary>
        /// Undoes the most recent lifecycle change, provided it happened inside the configured
        /// revert window.
        /// <para>
        /// Deliberately bypasses <see cref="EventLifecyclePolicy.CanTransition"/>: the reverse of
        /// a transition is frequently not itself a legal move — undoing a publish means going back
        /// to <c>Draft</c>, which is not, and should not be, a route organizers can take directly.
        /// Undo restores a state the event provably held moments ago, so the matrix does not apply.
        /// </para>
        /// </summary>
        public async Task<Events> RevertLastLifecycleChangeAsync(int eventId, int userId, string userRole)
        {
            try
            {
                var ev = await GetTrackedEventOrThrowAsync(eventId);
                await EnsureCanManageEventAsync(ev, userId, userRole);

                var now = GetUtcNow();

                if (ev.PreviousLifecycleState is null || ev.LifecycleChangedAt is null)
                    throw new BadRequestException("There is no recent lifecycle change to undo.");

                if (EventLifecyclePolicy.GetRevertAvailableUntil(
                        ev, now, _versioningOptions.LifecycleRevertWindowHours) is null)
                {
                    throw new BadRequestException(
                        "The window for undoing this change has passed. Use the lifecycle actions instead.");
                }

                var restoredState = ev.PreviousLifecycleState.Value;
                var previousSnapshot = BuildSnapshot(ev);

                ev.LifecycleState = restoredState;
                // Consumed: undo is a one-shot safety net, not a toggle to flip back and forth.
                ev.PreviousLifecycleState = null;
                ev.LifecycleChangedAt = now;
                ev.CurrentVersionNumber += 1;
                ev.UpdatedAt = now;

                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    AddVersionRecord(
                        ev,
                        EventVersionActions.LifecycleRevert,
                        actorUserId: userId,
                        actorRole: NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: BuildChangedFields(previousSnapshot, BuildSnapshot(ev)),
                        createdAt: now);

                    _outboxWriter.StageSync(ev);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                var withImages = await _eventsRepository.GetByIdAsync(eventId)
                    ?? throw new InternalServerErrorException("Failed to reload the reverted event.");

                await CacheEventAsync(withImages);
                await BumpEventListVersionAsync();

                if (restoredState == EventLifecycleState.Published && withImages.WaitlistEnabled)
                    await DrainWaitlistAsync(eventId);

                return withImages;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventsService] RevertLastLifecycleChangeAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<Events> GetTrackedEventOrThrowAsync(int eventId)
        {
            return await _db.Events
                .Include(e => e.Images)
                .FirstOrDefaultAsync(e => e.Id == eventId)
                ?? throw new ResourceNotFoundException($"Event {eventId} not found");
        }

        private async Task<Events> GetEventRecordOrThrowAsync(int eventId)
        {
            return await _db.Events
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == eventId)
                ?? throw new ResourceNotFoundException($"Event {eventId} not found");
        }

        private async Task EnsureCanManageEventAsync(Events ev, int userId, string userRole)
        {
            if (!await _clubService.CanManageClubAsync(ev.ClubId, userId, userRole))
                throw new ForbiddenException("Not allowed");
        }

        private async Task EnsureCanManageEventMediaAsync(Events ev, int userId, string userRole)
        {
            if (!await _clubService.CanManageEventMediaAsync(ev.ClubId, userId, userRole))
                throw new ForbiddenException("Not allowed");
        }

        private async Task EnsureCanManageClubAsync(int clubId, int userId, string userRole)
        {
            var club = await _clubService.GetClub(clubId);
            if (!await _clubService.CanManageClubAsync(club.Id, userId, userRole))
                throw new ForbiddenException("Not allowed");
        }

        private async Task EnsureCanManageAllClubIdsAsync(
            IEnumerable<int> clubIds,
            int userId,
            string userRole,
            string message)
        {
            foreach (var clubId in clubIds.Distinct())
            {
                if (!await _clubService.CanManageClubAsync(clubId, userId, userRole))
                    throw new ForbiddenException(message);
            }
        }

        // The version-history mechanics live in EventVersionRecorder so EventSeriesService
        // records byte-identical audit rows for the occurrences it mutates in bulk. These
        // shims keep every call site in this file — and the tests reflecting into them —
        // working unchanged.
        private void AddVersionRecord(
            Events ev,
            string actionType,
            int actorUserId,
            string actorRole,
            int? rollbackSourceVersionNumber,
            IReadOnlyList<EventVersionFieldChange> changedFields,
            DateTime createdAt) =>
            EventVersionRecorder.Add(
                _db,
                ev,
                actionType,
                actorUserId,
                actorRole,
                rollbackSourceVersionNumber,
                changedFields,
                createdAt);

        private static EventVersionSnapshot BuildSnapshot(Events ev) =>
            EventVersionRecorder.BuildSnapshot(ev);

        private static void ApplySnapshot(Events ev, EventVersionSnapshot snapshot) =>
            EventVersionRecorder.ApplySnapshot(ev, snapshot);

        private static void ApplyBatchPatch(Events ev, BatchUpdateEventItem item)
        {
            if (item.Name != null)
                ev.Name = item.Name;
            if (item.Description != null)
                ev.Description = item.Description;
            if (item.Location != null)
                ev.Location = item.Location;
            if (item.IsPrivate.HasValue)
                ev.isPrivate = item.IsPrivate.Value;
            if (item.MaxParticipants.HasValue)
                ev.maxParticipants = item.MaxParticipants.Value;
            if (item.RegisterCost.HasValue)
                ev.registerCost = item.RegisterCost.Value;
            if (item.StartTime.HasValue)
                ev.StartTime = item.StartTime.Value;
            if (item.EndTime.HasValue)
                ev.EndTime = item.EndTime.Value;
            if (item.Category.HasValue)
                ev.Category = item.Category.Value;
            if (item.VenueName != null)
                ev.VenueName = item.VenueName;
            if (item.City != null)
                ev.City = item.City;
            if (item.Latitude.HasValue)
                ev.Latitude = item.Latitude;
            if (item.Longitude.HasValue)
                ev.Longitude = item.Longitude;
            if (item.Tags != null)
                ev.Tags = NormalizeTags(item.Tags);
        }

        private static List<EventVersionFieldChange> BuildChangedFields(
            EventVersionSnapshot? previous,
            EventVersionSnapshot current) =>
            EventVersionRecorder.BuildChangedFields(previous, current);

        private EventVersionHistoryItem MapHistoryItem(EventVersion version, int currentVersionNumber) => new(
            version.EventId,
            version.VersionNumber,
            version.ActionType,
            version.CreatedAt,
            version.ActorUserId,
            version.ActorRole,
            IsRollbackEligible(version, currentVersionNumber),
            GetRollbackExpiry(version.CreatedAt),
            version.RollbackSourceVersionNumber,
            DeserializeChangedFields(version.ChangedFieldsJson));

        private bool IsRollbackEligible(EventVersion version, int currentVersionNumber)
        {
            if (version.VersionNumber == currentVersionNumber)
                return false;

            return GetRollbackExpiry(version.CreatedAt) >= GetUtcNow();
        }

        private DateTime GetRollbackExpiry(DateTime createdAt) =>
            createdAt.AddDays(_versioningOptions.RollbackWindowDays);

        private DateTime GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

        private static EventVersionSnapshot DeserializeSnapshot(string snapshotJson) =>
            JsonSerializer.Deserialize<EventVersionSnapshot>(snapshotJson)
            ?? throw new InvalidOperationException("Event version snapshot could not be deserialized.");

        private static IReadOnlyList<EventVersionFieldChange> DeserializeChangedFields(string changedFieldsJson) =>
            JsonSerializer.Deserialize<List<EventVersionFieldChange>>(changedFieldsJson) ?? [];

        private static bool IsAdminRole(string? userRole) =>
            string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeActorRole(string actorRole) =>
            EventVersionRecorder.NormalizeActorRole(actorRole);

        private static int NormalizePage(int page) => page < 1 ? 1 : page;

        private static int NormalizePageSize(int pageSize) => pageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => pageSize
        };

        // Kept as a private shim over the shared normalizer: EventsServiceTests reflects into
        // this member by name, and series-generated occurrences must normalize identically.
        private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
            EventTagNormalizer.Normalize(tags);

        /// <summary>
        /// Records that an occurrence has been edited in its own right, so a later
        /// "update all future occurrences" does not silently overwrite the change. Standalone
        /// events are unaffected.
        /// </summary>
        private static void MarkSeriesOverridden(Events ev)
        {
            if (ev.SeriesId.HasValue)
                ev.SeriesOverridden = true;
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

        // Delegates to the shared validator so the recurrence series feature enforces the
        // identical ownership and expiry checks on images it attaches to occurrences.
        private Task ValidateUploadedImageUrlsAsync(
            int clubId,
            int userId,
            IEnumerable<string> imageUrls,
            int? eventId = null,
            ISet<string>? existingUrls = null) =>
            EventImageUploadValidator.ValidateAsync(
                _blobService,
                _cache,
                clubId,
                userId,
                imageUrls,
                eventId,
                existingUrls);

        private static string GetImageUploadIntentKey(string imageUrl) =>
            EventImageUploadValidator.IntentKey(imageUrl);

        // The policy itself lives in EventAccessChecker so that components which cannot
        // depend on IEventsService (the waitlist promoter) can evaluate the same rules.
        private Task<bool> CanViewEventAsync(Events ev, int? userId, string? userRole)
            => _accessChecker.CanViewEventAsync(ev, userId, userRole);

        public async Task NotifyRegistrationChangedAsync(int eventId)
        {
            try
            {
                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                var found = await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync();

                    var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                    if (ev == null)
                        return false;

                    _outboxWriter.StageSync(ev);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                });

                if (!found)
                    return;

                await _refreshCache.RemoveAsync(EventCacheKeys.Event(eventId));
            }
            catch (Exception e)
            {
                Logger.Warn(e, $"[EventsService] NotifyRegistrationChangedAsync failed for event {eventId}");
            }
        }

        private Task CacheEventAsync(Events ev) =>
            _refreshCache.SetAsync(EventCacheKeys.Event(ev.Id), ev, EventTTL, EventCacheSerializerOptions);

        private async Task<long> GetEventListVersionAsync()
        {
            var v = await _cache.GetValueAsync(EventCacheKeys.ListVersion);
            if (v == null)
            {
                await _cache.SetValueAsync(EventCacheKeys.ListVersion, "1");
                return 1;
            }
            return long.Parse(v);
        }

        private async Task BumpEventListVersionAsync()
        {
            await _cache.IncrementAsync(EventCacheKeys.ListVersion);
        }

        /// <summary>
        /// Drains the waitlist after an edit that may have freed seats.
        ///
        /// Runs POST-COMMIT rather than inside the caller's transaction: UpdateEvent uses the
        /// default isolation level and its transaction also spans image writes and blob
        /// validation, so nesting promotion there would either force an isolation upgrade of a
        /// large transaction or run the capacity check at REPEATABLE READ. Correctness is
        /// preserved because PromoteStandaloneAsync re-reads capacity and the live registration
        /// count inside its OWN Serializable transaction while holding the event lock.
        ///
        /// Trade-off: a brief window where seats are free and nobody has been promoted. It is
        /// self-healing — the next unregister, capacity change, or the organizer's
        /// "Promote next" button drains it.
        /// </summary>
        private async Task TryPromoteWaitlistAsync(int eventId, int previousMax, bool previousWaitlistEnabled, Events current)
        {
            var capacityRaised =
                current.maxParticipants > previousMax ||             // e.g. 10 -> 20
                (previousMax > 0 && current.maxParticipants == 0);   // limited -> unlimited

            var waitlistJustEnabled = !previousWaitlistEnabled && current.WaitlistEnabled;

            if (!current.WaitlistEnabled || (!capacityRaised && !waitlistJustEnabled))
                return;

            await DrainWaitlistAsync(eventId);
        }

        /// <summary>
        /// Promotes waitlisted people into any free seats.
        /// <para>
        /// Also used when an event returns to <c>Published</c>: the promoter refuses to act on a
        /// paused or cancelled event, so seats freed while it was off sale stay queued until
        /// something reopens it.
        /// </para>
        /// </summary>
        private async Task DrainWaitlistAsync(int eventId)
        {
            try
            {
                // A single run is capped (it holds a 10s lock), so raising capacity by more than
                // one batch — or switching a populated event to unlimited — needs repeat passes;
                // otherwise the surplus would sit queued against empty seats until an unrelated
                // later trigger. Loop until a pass promotes nobody, bounded so a pathological
                // queue cannot spin here.
                const int maxPasses = 20;

                for (var pass = 0; pass < maxPasses; pass++)
                {
                    if (await _waitlistPromoter.PromoteStandaloneAsync(eventId) == 0)
                        break;
                }
            }
            catch (Exception e)
            {
                // Never fail the organizer's save because promotion could not run.
                Logger.Warn(e, $"[EventsService] Waitlist promotion failed for event {eventId}");
            }
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
