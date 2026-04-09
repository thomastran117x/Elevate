using System.Text.Json;

using backend.main.dtos.responses.eventregistration;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.EntityFrameworkCore;

namespace backend.main.services.implementation
{
    public class EventRegistrationService : IEventRegistrationService
    {
        private readonly IEventRegistrationRepository _registrationRepository;
        private readonly IEventsService _eventsService;
        private readonly ICacheService _cache;

        private static readonly TimeSpan MembershipTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ListTTL = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan LockTTL = TimeSpan.FromSeconds(10);
        private const string NullSentinel = "__null__";

        public EventRegistrationService(
            IEventRegistrationRepository registrationRepository,
            IEventsService eventsService,
            ICacheService cache)
        {
            _registrationRepository = registrationRepository;
            _eventsService = eventsService;
            _cache = cache;
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

                // Capacity check + insert are performed atomically inside a SERIALIZABLE
                // transaction, eliminating the race window between count and insert.
                // The Redis lock above reduces contention under normal load.
                var registration = await _registrationRepository.TryRegisterAsync(eventId, userId);

                if (registration == null)
                    throw new ConflictException("Event is full");

                await _cache.SetValueAsync(
                    MembershipKey(userId, eventId),
                    JsonSerializer.Serialize(registration),
                    MembershipTTL
                );
                await InvalidateListsAsync(userId, eventId);
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

                if (!await IsRegisteredAsync(eventId, userId))
                    throw new ResourceNotFoundException("Registration not found");

                await _registrationRepository.UnregisterAsync(eventId, userId);

                await _cache.DeleteKeyAsync(MembershipKey(userId, eventId));
                await InvalidateListsAsync(userId, eventId);
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
