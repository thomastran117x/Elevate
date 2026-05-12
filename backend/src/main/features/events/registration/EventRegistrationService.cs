using System.Data;
using System.Text.Json;

using backend.main.infrastructure.database.core;
using backend.main.features.events.registration.contracts.responses;
using backend.main.features.events.search;
using backend.main.shared.exceptions.http;
using backend.main.features.events.registration;
using backend.main.features.cache;

using Microsoft.EntityFrameworkCore;
using backend.main.shared.utilities.logger;

namespace backend.main.features.events.registration
{
    public class EventRegistrationService : IEventRegistrationService
    {
        private readonly AppDatabaseContext _db;
        private readonly IEventRegistrationRepository _registrationRepository;
        private readonly IEventsService _eventsService;
        private readonly ICacheService _cache;
        private readonly IEventSearchOutboxWriter _outboxWriter;

        private static readonly TimeSpan MembershipTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ListTTL = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan LockTTL = TimeSpan.FromSeconds(10);
        private const string NullSentinel = "__null__";

        public EventRegistrationService(
            AppDatabaseContext db,
            IEventRegistrationRepository registrationRepository,
            IEventsService eventsService,
            ICacheService cache,
            IEventSearchOutboxWriter outboxWriter)
        {
            _db = db;
            _registrationRepository = registrationRepository;
            _eventsService = eventsService;
            _cache = cache;
            _outboxWriter = outboxWriter;
        }

        public async Task<BatchRegistrationResultResponse> BatchRegisterAsync(int userId, IEnumerable<int> eventIds)
        {
            var result = new BatchRegistrationResultResponse();

            // Process sequentially — each registration acquires a per-event Redis lock
            // and a SERIALIZABLE DB transaction. Running in parallel would risk deadlocks
            // and excessive lock contention under concurrent batch requests.
            foreach (var eventId in eventIds)
            {
                try
                {
                    await RegisterAsync(eventId, userId);
                    result.Succeeded.Add(eventId);
                }
                catch (AppException ex)
                {
                    result.Failed.Add(new BatchRegistrationFailure
                    {
                        EventId = eventId,
                        Reason = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EventRegistrationService] BatchRegisterAsync failed for event {eventId}: {ex}");
                    result.Failed.Add(new BatchRegistrationFailure
                    {
                        EventId = eventId,
                        Reason = "An unexpected error occurred."
                    });
                }
            }

            return result;
        }

        public async Task<BatchRegistrationResultResponse> BatchUnregisterAsync(int userId, IEnumerable<int> eventIds)
        {
            var result = new BatchRegistrationResultResponse();

            foreach (var eventId in eventIds)
            {
                try
                {
                    await UnregisterAsync(eventId, userId);
                    result.Succeeded.Add(eventId);
                }
                catch (AppException ex)
                {
                    result.Failed.Add(new BatchRegistrationFailure
                    {
                        EventId = eventId,
                        Reason = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[EventRegistrationService] BatchUnregisterAsync failed for event {eventId}: {ex}");
                    result.Failed.Add(new BatchRegistrationFailure
                    {
                        EventId = eventId,
                        Reason = "An unexpected error occurred."
                    });
                }
            }

            return result;
        }

        private string LockKey(int eventId)
            => $"evtreg:lock:{eventId}";

        private string MembershipKey(int userId, int eventId)
            => $"evtreg:u:{userId}:e:{eventId}";

        private string EventListKey(int eventId, int page, int size)
            => $"evtreg:list:e:{eventId}:{page}:{size}";

        private string UserListKey(int userId, int page, int size)
            => $"evtreg:list:u:{userId}:{page}:{size}";

        // Reverse index sets — track which list cache keys exist per event/user
        // so invalidation is a Set read + targeted deletes rather than a full key scan.
        private string EventIndexKey(int eventId)
            => $"evtreg:index:e:{eventId}";

        private string UserIndexKey(int userId)
            => $"evtreg:index:u:{userId}";

        public async Task RegisterAsync(int eventId, int userId)
        {
            var ev = await _eventsService.GetEvent(eventId);

            if (ev.registerCost > 0)
                throw new BadRequestException("Paid events require checkout");

            var lockKey = LockKey(eventId);
            var lockValue = Guid.NewGuid().ToString();
            var acquired = await _cache.AcquireLockAsync(lockKey, lockValue, LockTTL);

            if (!acquired)
                throw new ConflictException("Event registration is busy, please try again");

            try
            {
                if (await IsRegisteredAsync(eventId, userId))
                    throw new ConflictException("Already registered for this event");

                await using var transaction = await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

                var trackedEvent = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                if (trackedEvent == null)
                    throw new ResourceNotFoundException($"Event {eventId} not found");

                if (trackedEvent.EndTime.HasValue && trackedEvent.EndTime <= DateTime.UtcNow)
                    throw new ConflictException("Event is full");

                var count = await _db.EventRegistrations.CountAsync(r => r.EventId == eventId);
                if (count >= trackedEvent.maxParticipants)
                    throw new ConflictException("Event is full");

                var registration = new EventRegistration
                {
                    EventId = eventId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.EventRegistrations.Add(registration);
                trackedEvent.RegistrationCount += 1;
                trackedEvent.UpdatedAt = DateTime.UtcNow;
                _outboxWriter.StageUpsert(trackedEvent);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _cache.SetValueAsync(
                    MembershipKey(userId, eventId),
                    JsonSerializer.Serialize(registration),
                    MembershipTTL
                );
                await InvalidateListsAsync(userId, eventId);
                await _cache.DeleteKeyAsync($"event:{eventId}");
            }
            catch (DbUpdateException)
            {
                // Unique constraint on (EventId, UserId) caught a duplicate that slipped past the cache check
                throw new ConflictException("Already registered for this event");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventRegistrationService] RegisterAsync failed: {e}");
                throw new InternalServerErrorException();
            }
            finally
            {
                await _cache.ReleaseLockAsync(lockKey, lockValue);
            }
        }

        public async Task UnregisterAsync(int eventId, int userId)
        {
            try
            {
                await _eventsService.GetEvent(eventId);

                await using var transaction = await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

                var registration = await _db.EventRegistrations
                    .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);

                if (registration == null)
                    throw new ResourceNotFoundException("Registration not found");

                _db.EventRegistrations.Remove(registration);

                var trackedEvent = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                if (trackedEvent != null)
                {
                    trackedEvent.RegistrationCount = Math.Max(0, trackedEvent.RegistrationCount - 1);
                    trackedEvent.UpdatedAt = DateTime.UtcNow;
                    _outboxWriter.StageUpsert(trackedEvent);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                await _cache.DeleteKeyAsync(MembershipKey(userId, eventId));
                await InvalidateListsAsync(userId, eventId);
                await _cache.DeleteKeyAsync($"event:{eventId}");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventRegistrationService] UnregisterAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<bool> IsRegisteredAsync(int eventId, int userId)
        {
            try
            {
                var key = MembershipKey(userId, eventId);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return cached != NullSentinel;

                var registration = await _registrationRepository.IsRegisteredAsync(eventId, userId);

                if (registration == null)
                {
                    await _cache.SetValueAsync(key, NullSentinel, MembershipTTL);
                    return false;
                }

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(registration), MembershipTTL);
                return true;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventRegistrationService] IsRegisteredAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20)
        {
            try
            {
                var key = EventListKey(eventId, page, pageSize);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return JsonSerializer.Deserialize<List<EventRegistration>>(cached)!;

                var registrations = (await _registrationRepository.GetRegistrationsByEventAsync(eventId, page, pageSize)).ToList();

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(registrations), ListTTL);
                await TrackEventListKeyAsync(eventId, key);

                return registrations;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventRegistrationService] GetRegistrationsByEventAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                var key = UserListKey(userId, page, pageSize);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return JsonSerializer.Deserialize<List<EventRegistration>>(cached)!;

                var registrations = (await _registrationRepository.GetRegistrationsByUserAsync(userId, page, pageSize)).ToList();

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(registrations), ListTTL);
                await TrackUserListKeyAsync(userId, key);

                return registrations;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventRegistrationService] GetRegistrationsByUserAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task TrackEventListKeyAsync(int eventId, string key)
        {
            var indexKey = EventIndexKey(eventId);
            await _cache.SetAddAsync(indexKey, key);
            await _cache.SetExpiryAsync(indexKey, ListTTL);
        }

        private async Task TrackUserListKeyAsync(int userId, string key)
        {
            var indexKey = UserIndexKey(userId);
            await _cache.SetAddAsync(indexKey, key);
            await _cache.SetExpiryAsync(indexKey, ListTTL);
        }

        private async Task InvalidateListsAsync(int userId, int eventId)
        {
            var eventIndexKey = EventIndexKey(eventId);
            var userIndexKey = UserIndexKey(userId);

            var eventKeys = await _cache.SetMembersAsync(eventIndexKey);
            var userKeys = await _cache.SetMembersAsync(userIndexKey);

            foreach (var key in eventKeys.Concat(userKeys))
                await _cache.DeleteKeyAsync(key);

            await _cache.DeleteKeyAsync(eventIndexKey);
            await _cache.DeleteKeyAsync(userIndexKey);
        }
    }
}


