using System.Data;

using backend.main.features.cache;
using backend.main.features.events.access;
using backend.main.features.events.contracts.responses;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.waitlist.contracts.requests;
using backend.main.features.events.waitlist.contracts.responses;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.waitlist
{
    /// <summary>
    /// Waitlist reads are deliberately NOT cached, unlike EventRegistrationService's
    /// refresh-ahead reads. A position is volatile — any leave shifts everyone below it — and
    /// a stale "you're #3" is a user-visible lie. All queries here are index-covered and tiny.
    /// The only cache touched is event:{id}, because WaitlistCount lives on the Events entity.
    /// </summary>
    public class EventWaitlistService : IEventWaitlistService
    {
        private readonly AppDatabaseContext _db;
        private readonly IEventWaitlistRepository _waitlistRepository;
        private readonly IEventWaitlistPromoter _promoter;
        private readonly IEventsService _eventsService;
        private readonly IEventAccessChecker _accessChecker;
        private readonly ICacheService _cache;
        private readonly IRefreshAheadCache _refreshCache;
        private readonly IEventSearchOutboxWriter _outboxWriter;
        private readonly IPublisher _publisher;

        private static readonly TimeSpan LockTTL = TimeSpan.FromSeconds(10);

        public EventWaitlistService(
            AppDatabaseContext db,
            IEventWaitlistRepository waitlistRepository,
            IEventWaitlistPromoter promoter,
            IEventsService eventsService,
            IEventAccessChecker accessChecker,
            ICacheService cache,
            IRefreshAheadCache refreshCache,
            IEventSearchOutboxWriter outboxWriter,
            IPublisher publisher)
        {
            _db = db;
            _waitlistRepository = waitlistRepository;
            _promoter = promoter;
            _eventsService = eventsService;
            _accessChecker = accessChecker;
            _cache = cache;
            _refreshCache = refreshCache;
            _outboxWriter = outboxWriter;
            _publisher = publisher;
        }

        private static string? Sanitize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public async Task<EventWaitlistEntryResponse> JoinAsync(
            int eventId, int userId, string userRole, JoinWaitlistRequest? request = null)
        {
            // Handles private-event visibility, including the isPrivate gate.
            await _eventsService.EnsureCanViewEventAsync(eventId, userId, userRole);

            // Same lock as registration: joining is gated on the live seat count, so it must
            // not interleave with register/unregister.
            var lockKey = EventRegistrationCacheKeys.Lock(eventId);
            var lockValue = Guid.NewGuid().ToString();

            if (!await _cache.AcquireLockAsync(lockKey, lockValue, LockTTL))
                throw new ConflictException("Event waitlist is busy, please try again");

            EventWaitlistEntry entry = null!;
            string? eventName = null;
            DateTime? eventStartsAtUtc = null;

            try
            {
                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                    var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId)
                        ?? throw new ResourceNotFoundException($"Event {eventId} not found");

                    if (!ev.WaitlistEnabled)
                        throw new BadRequestException("This event does not have a waitlist.");

                    if (!EventLifecyclePolicy.AllowsRegistration(ev.LifecycleState))
                        throw new ConflictException("The waitlist is only available for published events.");

                    if (ev.registerCost > 0)
                        throw new BadRequestException("Waitlists are not available for paid events.");

                    if (ev.maxParticipants <= 0)
                        throw new BadRequestException("This event has unlimited capacity — you can register directly.");

                    if (ev.StartTime.HasValue && ev.StartTime.Value <= DateTime.UtcNow)
                        throw new ConflictException("The waitlist is closed — the event has already started");

                    var alreadyRegistered = await _db.EventRegistrations
                        .AnyAsync(r => r.EventId == eventId && r.UserId == userId && r.Status == RegistrationStatus.Active);

                    if (alreadyRegistered)
                        throw new ConflictException("You're already registered for this event");

                    var activeCount = await _db.EventRegistrations
                        .CountAsync(r => r.EventId == eventId && r.Status == RegistrationStatus.Active);

                    if (activeCount < ev.maxParticipants)
                        throw new ConflictException("Seats are still available — register instead");

                    var now = DateTime.UtcNow;
                    var existing = await _db.EventWaitlistEntries
                        .FirstOrDefaultAsync(w => w.EventId == eventId && w.UserId == userId);

                    if (existing != null)
                    {
                        if (existing.Status == EventWaitlistEntryStatus.Waiting)
                            throw new ConflictException("You're already on the waitlist for this event");

                        // Reactivate in place — the unique (EventId, UserId) index forbids a second
                        // row. JoinedAtUtc is reset so a rejoin goes to the back of the queue.
                        existing.Status = EventWaitlistEntryStatus.Waiting;
                        existing.JoinedAtUtc = now;
                        existing.PromotedAtUtc = null;
                        existing.LeftAtUtc = null;
                        existing.RemovedAtUtc = null;
                        existing.RemovedByUserId = null;
                        existing.PromotionEmailQueuedAtUtc = null;
                        // A rejoin is a fresh eligibility claim; don't inherit an old cooldown.
                        existing.EligibilityDeferredUntilUtc = null;
                        existing.Notes = Sanitize(request?.Notes);
                        existing.PhoneNumber = Sanitize(request?.PhoneNumber);
                        existing.DietaryNeeds = Sanitize(request?.DietaryNeeds);
                        existing.UpdatedAt = now;
                        entry = existing;
                    }
                    else
                    {
                        entry = new EventWaitlistEntry
                        {
                            EventId = eventId,
                            UserId = userId,
                            Status = EventWaitlistEntryStatus.Waiting,
                            JoinedAtUtc = now,
                            Notes = Sanitize(request?.Notes),
                            PhoneNumber = Sanitize(request?.PhoneNumber),
                            DietaryNeeds = Sanitize(request?.DietaryNeeds),
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        _db.EventWaitlistEntries.Add(entry);
                    }

                    await _db.SaveChangesAsync();

                    ev.WaitlistCount = await _db.EventWaitlistEntries
                        .CountAsync(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting);
                    ev.UpdatedAt = now;
                    _outboxWriter.StageSync(ev);

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    eventName = ev.Name;
                    eventStartsAtUtc = ev.StartTime;
                });
            }
            catch (DbUpdateException)
            {
                // Unique (EventId, UserId) caught a racer that slipped past the lock.
                throw new ConflictException("You're already on the waitlist for this event");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventWaitlistService] JoinAsync failed: {e}");
                throw new InternalServerErrorException();
            }
            finally
            {
                await _cache.ReleaseLockAsync(lockKey, lockValue);
            }

            await _refreshCache.RemoveAsync($"event:{eventId}");

            try
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    await _publisher.PublishAsync(NotificationTopics.Email, new EmailMessage
                    {
                        Type = EmailMessageType.WaitlistJoined,
                        Email = user.Email,
                        RecipientName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                        EventId = eventId,
                        EventName = eventName,
                        EventStartsAtUtc = eventStartsAtUtc
                    });
                }
            }
            catch (Exception e)
            {
                // The user IS on the waitlist; a missing confirmation email must not fail the join.
                Logger.Warn(e, $"[EventWaitlistService] Failed to publish waitlist-joined email for user {userId}");
            }

            var position = await _waitlistRepository.GetPositionAsync(eventId, entry.JoinedAtUtc, entry.Id);
            return MapToResponse(entry, position);
        }

        public async Task LeaveAsync(int eventId, int userId, string userRole)
        {
            // Deliberately NOT gated on EnsureCanViewEventAsync. The promoter leaves entries
            // Waiting when a private-event invitation is revoked, so requiring current
            // visibility here would trap those users in the queue with their phone number and
            // dietary notes still stored, removable only by an organizer. Owning the entry is
            // sufficient authority to withdraw from it.
            var lockKey = EventRegistrationCacheKeys.Lock(eventId);
            var lockValue = Guid.NewGuid().ToString();

            if (!await _cache.AcquireLockAsync(lockKey, lockValue, LockTTL))
                throw new ConflictException("Event waitlist is busy, please try again");

            try
            {
                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                    var entry = await _db.EventWaitlistEntries
                        .FirstOrDefaultAsync(w =>
                            w.EventId == eventId &&
                            w.UserId == userId &&
                            w.Status == EventWaitlistEntryStatus.Waiting)
                        ?? throw new ResourceNotFoundException("You're not on the waitlist for this event");

                    var now = DateTime.UtcNow;
                    entry.Status = EventWaitlistEntryStatus.Left;
                    entry.LeftAtUtc = now;
                    entry.UpdatedAt = now;

                    await _db.SaveChangesAsync();
                    await UpdateWaitlistCountAsync(eventId, now);

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventWaitlistService] LeaveAsync failed: {e}");
                throw new InternalServerErrorException();
            }
            finally
            {
                await _cache.ReleaseLockAsync(lockKey, lockValue);
            }

            await _refreshCache.RemoveAsync($"event:{eventId}");
        }

        public async Task<MyWaitlistStatusResponse> GetMyStatusAsync(int eventId, int userId, string userRole)
        {
            await _eventsService.EnsureCanViewEventAsync(eventId, userId, userRole);

            var waitlistCount = await _waitlistRepository.CountWaitingAsync(eventId);
            var entry = await _waitlistRepository.GetEntryAsync(eventId, userId);

            if (entry == null || entry.Status != EventWaitlistEntryStatus.Waiting)
            {
                return new MyWaitlistStatusResponse
                {
                    OnWaitlist = false,
                    WaitlistCount = waitlistCount
                };
            }

            return new MyWaitlistStatusResponse
            {
                OnWaitlist = true,
                EntryId = entry.Id,
                Position = await _waitlistRepository.GetPositionAsync(eventId, entry.JoinedAtUtc, entry.Id),
                JoinedAtUtc = entry.JoinedAtUtc,
                WaitlistCount = waitlistCount
            };
        }

        public async Task<(IReadOnlyList<EventWaitlistEntryResponse> Entries, int TotalCount)> GetEventWaitlistAsync(
            int eventId, int actorUserId, string actorRole, int page = 1, int pageSize = 20)
        {
            // Throws Forbidden/NotFound unless the caller can manage the event.
            await _eventsService.GetManageableEvent(eventId, actorUserId, actorRole);

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var totalCount = await _waitlistRepository.CountWaitingAsync(eventId);
            var entries = await _waitlistRepository.GetWaitingByEventAsync(eventId, page, pageSize);

            var userIds = entries.Select(e => e.UserId).Distinct().ToList();
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.Name, u.Username })
                .ToDictionaryAsync(u => u.Id);

            var responses = entries.Select((entry, index) =>
            {
                // The roster is a contiguous ordered page, so positions follow from the offset
                // rather than needing a COUNT per row.
                var response = MapToResponse(entry, (page - 1) * pageSize + index + 1);

                if (users.TryGetValue(entry.UserId, out var user))
                {
                    response.UserName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
                    response.UserEmail = user.Email;
                }

                // Caller is a manager by construction, so PII is included.
                response.Notes = entry.Notes;
                response.PhoneNumber = entry.PhoneNumber;
                response.DietaryNeeds = entry.DietaryNeeds;
                return response;
            }).ToList();

            return (responses, totalCount);
        }

        public async Task RemoveEntryAsync(int eventId, int entryId, int actorUserId, string actorRole)
        {
            await _eventsService.GetManageableEvent(eventId, actorUserId, actorRole);

            var lockKey = EventRegistrationCacheKeys.Lock(eventId);
            var lockValue = Guid.NewGuid().ToString();

            if (!await _cache.AcquireLockAsync(lockKey, lockValue, LockTTL))
                throw new ConflictException("Event waitlist is busy, please try again");

            try
            {
                // The context enables retry-on-failure, and that strategy rejects a
                // user-initiated transaction unless the whole unit runs through it.
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                    var entry = await _db.EventWaitlistEntries
                        .FirstOrDefaultAsync(w =>
                            w.EventId == eventId &&
                            w.Id == entryId &&
                            w.Status == EventWaitlistEntryStatus.Waiting)
                        ?? throw new ResourceNotFoundException($"Waitlist entry {entryId} not found");

                    var now = DateTime.UtcNow;
                    entry.Status = EventWaitlistEntryStatus.Removed;
                    entry.RemovedAtUtc = now;
                    entry.RemovedByUserId = actorUserId;
                    entry.UpdatedAt = now;

                    await _db.SaveChangesAsync();
                    await UpdateWaitlistCountAsync(eventId, now);

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                });
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[EventWaitlistService] RemoveEntryAsync failed: {e}");
                throw new InternalServerErrorException();
            }
            finally
            {
                await _cache.ReleaseLockAsync(lockKey, lockValue);
            }

            await _refreshCache.RemoveAsync($"event:{eventId}");
        }

        public async Task<IReadOnlyList<WaitlistedEventResponse>> GetMyWaitlistsAsync(int userId, string userRole)
        {
            var entries = await _waitlistRepository.GetWaitingByUserAsync(userId);
            if (entries.Count == 0)
                return [];

            var eventIds = entries.Select(e => e.EventId).Distinct().ToList();
            var events = await _db.Events
                .AsNoTracking()
                .Include(e => e.Images)
                .Where(e => eventIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            var results = new List<WaitlistedEventResponse>(entries.Count);

            foreach (var entry in entries)
            {
                if (!events.TryGetValue(entry.EventId, out var ev))
                    continue;

                // This response embeds full event details, so it must clear the same visibility
                // policy as any other event read — otherwise a user whose private-event
                // invitation was revoked after queueing keeps an open window onto that event.
                //
                // The row is redacted rather than dropped, though: this endpoint and the event
                // detail page are the only two places the UI can offer "leave waitlist", and the
                // detail page rejects them under the same policy. Omitting it would leave them
                // queued with their phone number and dietary notes stored and no way to withdraw.
                var canView = await _accessChecker.CanViewEventAsync(ev, userId, userRole);

                results.Add(new WaitlistedEventResponse
                {
                    EntryId = entry.Id,
                    Position = await _waitlistRepository.GetPositionAsync(entry.EventId, entry.JoinedAtUtc, entry.Id),
                    JoinedAtUtc = entry.JoinedAtUtc,
                    AccessRevoked = !canView,
                    Event = canView ? EventMapper.MapToResponse(ev) : RedactEvent(ev)
                });
            }

            return results;
        }

        public async Task<WaitlistPromotionResultResponse> PromoteNextAsync(int eventId, int actorUserId, string actorRole)
        {
            await _eventsService.GetManageableEvent(eventId, actorUserId, actorRole);

            var promotedCount = await _promoter.PromoteStandaloneAsync(eventId);

            if (promotedCount == 0)
                throw new ConflictException("No seats are available to promote into.");

            var promotedUserIds = await _db.EventWaitlistEntries
                .AsNoTracking()
                .Where(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Promoted)
                .OrderByDescending(w => w.PromotedAtUtc)
                .Take(promotedCount)
                .Select(w => w.UserId)
                .ToListAsync();

            return new WaitlistPromotionResultResponse
            {
                PromotedCount = promotedCount,
                PromotedUserIds = promotedUserIds
            };
        }

        /// <summary>
        /// Everything except the id stripped: enough for the client to call leave, and nothing
        /// that would disclose a private event the user may no longer see.
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

        private async Task UpdateWaitlistCountAsync(int eventId, DateTime now)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null)
                return;

            ev.WaitlistCount = await _db.EventWaitlistEntries
                .CountAsync(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting);
            ev.UpdatedAt = now;
            _outboxWriter.StageSync(ev);
        }

        private static EventWaitlistEntryResponse MapToResponse(EventWaitlistEntry entry, int position) => new()
        {
            Id = entry.Id,
            EventId = entry.EventId,
            UserId = entry.UserId,
            Position = entry.Status == EventWaitlistEntryStatus.Waiting ? position : 0,
            Status = entry.Status.ToString(),
            JoinedAtUtc = entry.JoinedAtUtc,
            PromotedAtUtc = entry.PromotedAtUtc,
            LeftAtUtc = entry.LeftAtUtc,
            RemovedAtUtc = entry.RemovedAtUtc
        };
    }
}
