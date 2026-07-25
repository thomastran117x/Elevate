using System.Data;

using backend.main.features.cache;
using backend.main.features.events.access;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.waitlist
{
    public sealed class EventWaitlistPromoter : IEventWaitlistPromoter
    {
        private readonly AppDatabaseContext _db;
        private readonly IEventAccessChecker _accessChecker;
        private readonly ICacheService _cache;
        private readonly IRefreshAheadCache _refreshCache;
        private readonly IEventSearchOutboxWriter _outboxWriter;
        private readonly IPublisher _publisher;

        /// <summary>
        /// Bounds the work done while holding a 10s Redis lock. A run that promotes more than
        /// this drains the rest on the next trigger.
        /// </summary>
        private const int MaxPromotionsPerRun = 50;

        /// <summary>
        /// Bounds the transaction when many candidates are skipped (disabled accounts, lost
        /// visibility) rather than promoted.
        /// </summary>
        private const int MaxScanPerRun = 200;

        private static readonly TimeSpan LockTTL = TimeSpan.FromSeconds(10);

        public EventWaitlistPromoter(
            AppDatabaseContext db,
            IEventAccessChecker accessChecker,
            ICacheService cache,
            IRefreshAheadCache refreshCache,
            IEventSearchOutboxWriter outboxWriter,
            IPublisher publisher)
        {
            _db = db;
            _accessChecker = accessChecker;
            _cache = cache;
            _refreshCache = refreshCache;
            _outboxWriter = outboxWriter;
            _publisher = publisher;
        }

        public async Task<IReadOnlyList<WaitlistPromotion>> PromoteWithinTransactionAsync(int eventId, DateTime nowUtc)
        {
            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null || !ev.WaitlistEnabled)
                return [];

            // Cancelled, archived and draft events never promote.
            if (ev.LifecycleState != EventLifecycleState.Published)
                return [];

            // Paid events are out of scope — their capacity is enforced at checkout, not here.
            if (ev.registerCost > 0)
                return [];

            if (ev.StartTime.HasValue && ev.StartTime.Value <= nowUtc)
                return [];

            var activeCount = await _db.EventRegistrations
                .CountAsync(r => r.EventId == eventId && r.Status == RegistrationStatus.Active);

            // maxParticipants == 0 means unlimited. Nobody should be queued on such an event,
            // but capacity *lowered to* unlimited can strand a queue, so drain it.
            var seats = ev.maxParticipants > 0
                ? ev.maxParticipants - activeCount
                : MaxPromotionsPerRun;

            if (seats <= 0)
                return [];

            seats = Math.Min(seats, MaxPromotionsPerRun);

            var promoted = new List<WaitlistPromotion>();

            // Paged scan. Entry mutations below are NOT flushed until the end, so every query
            // still sees the whole queue as Waiting in exactly the same (JoinedAtUtc, Id) order
            // — which is precisely why Skip(examined) advances correctly and why re-querying
            // without it would reprocess the same rows and double-register them.
            //
            // Paging (rather than one fixed Take) matters because skipped entries stay Waiting:
            // with a fixed window, a long run of ineligible candidates at the head of the queue
            // would be re-selected and re-skipped on every trigger, starving everyone behind
            // them indefinitely while seats sat empty.
            var examined = 0;

            while (promoted.Count < seats && examined < MaxScanPerRun)
            {
                var batchSize = Math.Min(seats - promoted.Count + 10, MaxScanPerRun - examined);

                var batch = await _db.EventWaitlistEntries
                    .Where(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting)
                    .OrderBy(w => w.JoinedAtUtc)
                    .ThenBy(w => w.Id)
                    .Skip(examined)
                    .Take(batchSize)
                    .ToListAsync();

                if (batch.Count == 0)
                    break;

                foreach (var entry in batch)
                {
                    if (promoted.Count >= seats)
                        break;

                    examined++;

                    var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == entry.UserId);

                    // Skip in place rather than closing the entry: both this and the visibility
                    // check below are reversible, and a removed entry could not be restored.
                    if (user == null || user.IsDisabled)
                        continue;

                    // Private events: access can be revoked after joining the queue.
                    if (!await _accessChecker.CanViewEventAsync(ev, entry.UserId, user.Usertype))
                        continue;

                    var existing = await _db.EventRegistrations
                        .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == entry.UserId);

                    if (existing is { Status: RegistrationStatus.Active })
                    {
                        // Registered directly while queued. Close the entry but do NOT consume
                        // the seat, otherwise the freed seat is burned on a no-op.
                        entry.Status = EventWaitlistEntryStatus.Promoted;
                        entry.PromotedAtUtc = nowUtc;
                        entry.UpdatedAt = nowUtc;
                        continue;
                    }

                    if (existing != null)
                    {
                        // A cancelled row exists — reactivate it. Inserting would violate the
                        // unique (EventId, UserId) index and fail the caller's transaction.
                        existing.Status = RegistrationStatus.Active;
                        existing.CancelledAt = null;
                        existing.Notes = entry.Notes;
                        existing.PhoneNumber = entry.PhoneNumber;
                        existing.DietaryNeeds = entry.DietaryNeeds;
                    }
                    else
                    {
                        _db.EventRegistrations.Add(new EventRegistration
                        {
                            EventId = eventId,
                            UserId = entry.UserId,
                            CreatedAt = nowUtc,
                            Status = RegistrationStatus.Active,
                            Notes = entry.Notes,
                            PhoneNumber = entry.PhoneNumber,
                            DietaryNeeds = entry.DietaryNeeds
                        });
                    }

                    entry.Status = EventWaitlistEntryStatus.Promoted;
                    entry.PromotedAtUtc = nowUtc;
                    entry.UpdatedAt = nowUtc;

                    promoted.Add(new WaitlistPromotion(
                        entry.Id,
                        entry.UserId,
                        user.Email,
                        string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name));
                }
            }

            // Flush inside the caller's transaction so its own counter recomputation is accurate.
            await _db.SaveChangesAsync();
            return promoted;
        }

        public async Task<int> PromoteStandaloneAsync(int eventId)
        {
            var lockKey = EventRegistrationCacheKeys.Lock(eventId);
            var lockValue = Guid.NewGuid().ToString();

            if (!await _cache.AcquireLockAsync(lockKey, lockValue, LockTTL))
            {
                Logger.Info($"[EventWaitlistPromoter] Skipping promotion for event {eventId} — lock busy.");
                return 0;
            }

            IReadOnlyList<WaitlistPromotion> promotions = [];
            string? eventName = null;
            DateTime? startsAtUtc = null;

            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var now = DateTime.UtcNow;
                promotions = await PromoteWithinTransactionAsync(eventId, now);

                var trackedEvent = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                if (trackedEvent != null)
                {
                    eventName = trackedEvent.Name;
                    startsAtUtc = trackedEvent.StartTime;

                    trackedEvent.RegistrationCount = await _db.EventRegistrations
                        .CountAsync(r => r.EventId == eventId && r.Status == RegistrationStatus.Active);
                    trackedEvent.WaitlistCount = await _db.EventWaitlistEntries
                        .CountAsync(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting);
                    trackedEvent.UpdatedAt = now;
                    _outboxWriter.StageSync(trackedEvent);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception e)
            {
                // Never throw: callers are on the unregister and event-update hot paths.
                Logger.Warn(e, $"[EventWaitlistPromoter] PromoteStandaloneAsync failed for event {eventId}");
                return 0;
            }
            finally
            {
                await _cache.ReleaseLockAsync(lockKey, lockValue);
            }

            await _refreshCache.RemoveAsync($"event:{eventId}");
            await InvalidateForPromotedAsync(promotions, eventId);
            await PublishPromotionEmailsAsync(promotions, eventId, eventName, startsAtUtc);

            return promotions.Count;
        }

        public async Task PublishPromotionEmailsAsync(
            IReadOnlyList<WaitlistPromotion> promotions,
            int eventId,
            string? eventName,
            DateTime? startsAtUtc)
        {
            if (promotions.Count == 0)
                return;

            var delivered = new List<int>(promotions.Count);

            foreach (var promotion in promotions)
            {
                try
                {
                    await _publisher.PublishAsync(NotificationTopics.Email, new EmailMessage
                    {
                        Type = EmailMessageType.WaitlistPromoted,
                        Email = promotion.Email,
                        RecipientName = promotion.RecipientName,
                        EventId = eventId,
                        EventName = eventName,
                        EventStartsAtUtc = startsAtUtc
                    });

                    delivered.Add(promotion.EntryId);
                }
                catch (Exception e)
                {
                    // The promotion is already committed and the user IS registered, so failing
                    // here would be strictly worse than a missing email. The entry keeps a null
                    // PromotionEmailQueuedAtUtc, which is the ONLY signal that the notification
                    // never went out: the entry is terminally Promoted, so neither automatic
                    // promotion nor the organizer's "Promote next" (both of which scan only
                    // Waiting entries) can re-drive it. Resending requires a reconciliation pass
                    // over Promoted entries with a null marker.
                    Logger.Warn(e, $"[EventWaitlistPromoter] Failed to publish promotion email for entry {promotion.EntryId}");
                }
            }

            if (delivered.Count == 0)
                return;

            try
            {
                // Recorded only after a successful publish, so the marker means "handed to the
                // publisher", not merely "we intended to".
                await _db.EventWaitlistEntries
                    .Where(w => delivered.Contains(w.Id))
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(w => w.PromotionEmailQueuedAtUtc, DateTime.UtcNow));
            }
            catch (Exception e)
            {
                Logger.Warn(e, "[EventWaitlistPromoter] Failed to record promotion email delivery markers");
            }
        }

        public async Task InvalidateForPromotedAsync(IReadOnlyList<WaitlistPromotion> promotions, int eventId)
        {
            foreach (var promotion in promotions)
            {
                try
                {
                    await _refreshCache.RemoveAsync(EventRegistrationCacheKeys.Membership(promotion.UserId, eventId));
                    await EventRegistrationCacheKeys.InvalidateListsAsync(_cache, promotion.UserId, eventId);
                }
                catch (Exception e)
                {
                    Logger.Warn(e, $"[EventWaitlistPromoter] Failed to invalidate caches for promoted user {promotion.UserId}");
                }
            }
        }
    }
}
