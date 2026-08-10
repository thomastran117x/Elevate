using backend.main.features.cache;
using backend.main.features.events.access;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.favourites.contracts.responses;
using backend.main.features.events.registration;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.favourites
{
    /// <summary>
    /// Stars are deliberately much cheaper than registrations. Favouriting does not consume a
    /// seat, so unlike EventRegistrationService this takes no per-event Redis mutex, opens no
    /// Serializable transaction, and stages no search-outbox row — none of an event's searchable
    /// state changes. It is a plain insert or delete behind the same visibility policy.
    /// </summary>
    public class EventFavouriteService : IEventFavouriteService
    {
        private readonly AppDatabaseContext _db;
        private readonly IEventFavouriteRepository _favouriteRepository;
        private readonly IEventsService _eventsService;
        private readonly IEventAccessChecker _accessChecker;
        private readonly IRefreshAheadCache _refreshCache;

        public EventFavouriteService(
            AppDatabaseContext db,
            IEventFavouriteRepository favouriteRepository,
            IEventsService eventsService,
            IEventAccessChecker accessChecker,
            IRefreshAheadCache refreshCache)
        {
            _db = db;
            _favouriteRepository = favouriteRepository;
            _eventsService = eventsService;
            _accessChecker = accessChecker;
            _refreshCache = refreshCache;
        }

        public async Task<EventFavouriteResponse> FavouriteAsync(int eventId, int userId, string userRole)
        {
            // Handles private-event visibility, including the isPrivate gate. A user must be
            // able to see an event before they can star it.
            await _eventsService.EnsureCanViewEventAsync(eventId, userId, userRole);

            try
            {
                var existing = await _db.EventFavourites
                    .FirstOrDefaultAsync(f => f.EventId == eventId && f.UserId == userId);

                if (existing != null)
                    return MapToResponse(existing);

                var favourite = new EventFavourite
                {
                    EventId = eventId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _db.EventFavourites.Add(favourite);

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Unique (EventId, UserId) caught a concurrent star from the same user —
                    // a double-tap on the toggle. That is the outcome they asked for, so read
                    // the winning row back rather than surfacing a conflict.
                    _db.Entry(favourite).State = EntityState.Detached;

                    var winner = await _db.EventFavourites
                        .AsNoTracking()
                        .FirstOrDefaultAsync(f => f.EventId == eventId && f.UserId == userId)
                        ?? throw new InternalServerErrorException();

                    await EventFavouriteCacheKeys.InvalidateUserAsync(_refreshCache, userId);
                    return MapToResponse(winner);
                }

                await EventFavouriteCacheKeys.InvalidateUserAsync(_refreshCache, userId);
                return MapToResponse(favourite);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventFavouriteService] FavouriteAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task UnfavouriteAsync(int eventId, int userId)
        {
            // Deliberately NOT gated on EnsureCanViewEventAsync, for the same reason waitlist
            // Leave is not: if a private-event invitation is revoked after starring, requiring
            // current visibility would leave the star permanently stuck. Owning it is enough
            // authority to remove it.
            try
            {
                // A single atomic DELETE rather than load-then-remove. Two overlapping requests
                // for the same star — a retry, or a double-tap — would otherwise both load the
                // row, and the loser's SaveChangesAsync would affect zero rows and throw
                // DbUpdateConcurrencyException, turning a documented-idempotent call into a 500.
                var removed = await _db.EventFavourites
                    .Where(f => f.EventId == eventId && f.UserId == userId)
                    .ExecuteDeleteAsync();

                // Idempotent: unstarring something already unstarred is the state they wanted,
                // and there is nothing to invalidate.
                if (removed == 0)
                    return;

                await EventFavouriteCacheKeys.InvalidateUserAsync(_refreshCache, userId);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventFavouriteService] UnfavouriteAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<EventFavouriteResponse> GetMyStatusAsync(int eventId, int userId)
        {
            var favourite = await _favouriteRepository.GetAsync(eventId, userId);

            return favourite == null
                ? new EventFavouriteResponse { EventId = eventId, IsFavourited = false }
                : MapToResponse(favourite);
        }

        public async Task<IReadOnlyList<int>> GetFavouriteEventIdsAsync(int userId)
        {
            return await _favouriteRepository.GetEventIdsByUserAsync(userId);
        }

        public async Task<IReadOnlyList<PinnedEventResponse>> GetMyPinnedAsync(int userId, string userRole)
        {
            var favourites = await _favouriteRepository.GetByUserAsync(userId);

            // Read registrations off the context rather than through IEventRegistrationService:
            // it keeps this page working when the events.registration flag is off (the "Going"
            // group is simply empty) and avoids paging through an API to get a full set.
            var registrations = await _db.EventRegistrations
                .AsNoTracking()
                .Where(r => r.UserId == userId && r.Status == RegistrationStatus.Active)
                .Select(r => new { r.EventId, r.CreatedAt })
                .ToListAsync();

            var favouritedAt = favourites.ToDictionary(f => f.EventId, f => f.CreatedAt);
            var registeredAt = registrations
                .GroupBy(r => r.EventId)
                .ToDictionary(g => g.Key, g => g.Min(r => r.CreatedAt));

            var eventIds = favouritedAt.Keys.Union(registeredAt.Keys).ToList();
            if (eventIds.Count == 0)
                return [];

            var events = await _db.Events
                .AsNoTracking()
                .Include(e => e.Images)
                .Where(e => eventIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            var results = new List<PinnedEventResponse>(eventIds.Count);

            foreach (var eventId in eventIds)
            {
                if (!events.TryGetValue(eventId, out var ev))
                    continue;

                // Same policy as the waitlist "my rows" endpoint: this response embeds full
                // event details, so it must clear the normal visibility gate. Redacted rather
                // than dropped, because this page is the only place the UI can unstar a row.
                var canView = await _accessChecker.CanViewEventAsync(ev, userId, userRole);

                results.Add(new PinnedEventResponse
                {
                    IsRegistered = registeredAt.ContainsKey(eventId),
                    IsFavourited = favouritedAt.ContainsKey(eventId),
                    FavouritedAtUtc = favouritedAt.TryGetValue(eventId, out var favedAt) ? favedAt : null,
                    RegisteredAtUtc = registeredAt.TryGetValue(eventId, out var regAt) ? regAt : null,
                    AccessRevoked = !canView,
                    Event = canView ? EventMapper.MapToResponse(ev) : RedactEvent(ev)
                });
            }

            // Going first, then Saved; soonest start time within each group. Events with no
            // start time sort last rather than first, which is what DateTime.MaxValue buys.
            return results
                .OrderByDescending(r => r.IsRegistered)
                .ThenBy(r => events[r.Event.Id].StartTime ?? DateTime.MaxValue)
                .ThenBy(r => r.Event.Id)
                .ToList();
        }

        /// <summary>
        /// Everything except the id stripped: enough for the client to call unfavourite, and
        /// nothing that would disclose a private event the user may no longer see.
        /// </summary>
        private static EventResponse RedactEvent(Events ev) => new()
        {
            Id = ev.Id,
            Name = string.Empty,
            Description = string.Empty,
            Location = string.Empty,
            ImageUrls = [],
            Tags = []
        };

        private static EventFavouriteResponse MapToResponse(EventFavourite favourite) => new()
        {
            EventId = favourite.EventId,
            IsFavourited = true,
            FavouritedAtUtc = favourite.CreatedAt
        };
    }
}
