using System.Text.Json;

using backend.main.dtos;
using backend.main.exceptions.http;
using backend.main.Mappers;
using backend.main.models.core;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;


namespace backend.main.services.implementation
{
    public class EventsService : IEventsService
    {
        private readonly IEventsRepository _eventsRepository;
        private readonly IClubService _clubService;
        private readonly IFileUploadService _fileUploadService;
        private readonly ICacheService _cache;

        private static readonly TimeSpan EventTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EventListTTL = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);
        private const string EventListVersionKey = "events:version";
        private const string NullSentinel = "__null__";

        public EventsService(
            IEventsRepository eventsRepository,
            IClubService clubService,
            IFileUploadService fileUploadService,
            ICacheService cache)
        {
            _eventsRepository = eventsRepository;
            _clubService = clubService;
            _fileUploadService = fileUploadService;
            _cache = cache;
        }

        public async Task<Events> CreateEvent(
            int clubId,
            int userId,
            string name,
            string description,
            string location,
            IFormFile image,
            DateTime startTime,
            DateTime? endTime,
            bool isPrivate = false,
            int maxParticipants = 100,
            int registerCost = 0)
        {
            try
            {
                await _clubService.GetClub(clubId);

                var imageUrl = await _fileUploadService.UploadImageAsync(image, "events")
                    ?? throw new InternalServerErrorException("Image upload failed");

                var ev = new Events
                {
                    Name = name,
                    Description = description,
                    Location = location,
                    ImageUrl = imageUrl,
                    StartTime = startTime,
                    EndTime = endTime,
                    isPrivate = isPrivate,
                    maxParticipants = maxParticipants,
                    registerCost = registerCost,
                    ClubId = clubId
                };

                var created = await _eventsRepository.CreateAsync(ev);

                await CacheEventAsync(created);
                await BumpEventListVersionAsync();

                return created;
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
            bool isAvailable = true,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var version = await GetEventListVersionAsync();

                var normalized = search?.Trim().ToLowerInvariant();

                var key = normalized == null
                    ? $"events:list:v{version}:p{page}:s{pageSize}"
                    : $"events:list:v{version}:q:{normalized}:p{page}:s{pageSize}";

                var cached = await _cache.GetValueAsync(key);
                if (cached != null)
                    return JsonSerializer.Deserialize<List<Events>>(cached)!;

                var events = await _eventsRepository.SearchAsync(
                    normalized,
                    isPrivate,
                    isAvailable,
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
            bool isPrivate = false,
            bool isAvailable = true,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                await _clubService.GetClub(clubId);

                return await _eventsRepository.SearchAsync(
                    null,
                    false,
                    true,
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
            IFormFile image,
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

                var newImage = await _fileUploadService.UploadImageAsync(image, "events")
                    ?? throw new InternalServerErrorException("Image upload failed");

                var updated = await _eventsRepository.UpdateAsync(eventId, new Events
                {
                    Name = name,
                    Description = description,
                    Location = location,
                    ImageUrl = newImage,
                    StartTime = startTime,
                    EndTime = endTime,
                    isPrivate = isPrivate,
                    maxParticipants = maxParticipants,
                    registerCost = registerCost,
                    ClubId = club.Id
                }) ?? throw new InternalServerErrorException("Update failed");

                await CacheEventAsync(updated);
                await BumpEventListVersionAsync();

                if (!string.IsNullOrWhiteSpace(existing.ImageUrl))
                    _ = _fileUploadService.DeleteImageAsync(existing.ImageUrl);

                return updated;
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

                await _fileUploadService.DeleteImageAsync(ev.ImageUrl);
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
