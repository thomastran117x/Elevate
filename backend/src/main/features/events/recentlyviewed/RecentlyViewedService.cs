using backend.main.features.events.recentlyviewed.contracts.requests;
using backend.main.features.events.recentlyviewed.contracts.responses;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.main.features.events.recentlyviewed
{
    /// <summary>
    /// A user's browsing history over events.
    /// <para>
    /// A history is a behavioural profile, so this slice is deliberately conservative: nothing is
    /// recorded that the user could not see at the time, entries they can no longer see are
    /// dropped from reads rather than redacted, and no method logs an event id next to a user id.
    /// </para>
    /// </summary>
    public class RecentlyViewedService : IRecentlyViewedService
    {
        private readonly AppDatabaseContext _db;
        private readonly IRecentlyViewedRepository _repository;
        private readonly IEventsService _eventsService;
        private readonly RecentlyViewedOptions _options;
        private readonly TimeProvider _timeProvider;

        public RecentlyViewedService(
            AppDatabaseContext db,
            IRecentlyViewedRepository repository,
            IEventsService eventsService,
            IOptions<RecentlyViewedOptions> options,
            TimeProvider timeProvider)
        {
            _db = db;
            _repository = repository;
            _eventsService = eventsService;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public async Task<RecordEventViewResponse> RecordViewAsync(int eventId, int userId, string userRole)
        {
            // Gates private events and lifecycle-hidden ones. Recording something the user cannot
            // see would leak its existence straight back to them on the history page.
            await _eventsService.EnsureCanViewEventAsync(eventId, userId, userRole);

            try
            {
                var settings = await GetSettingsAsync(userId);
                if (!settings.Enabled)
                    return new RecordEventViewResponse { EventId = eventId, Recorded = false };

                var now = _timeProvider.GetUtcNow().UtcDateTime;

                // Routing the repeat view through the unique index first is what keeps the common
                // case - revisiting something already in the history - down to one statement.
                var updated = await _db.RecentlyViewedEvents
                    .Where(v => v.UserId == userId && v.EventId == eventId)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewedAt, now));

                if (updated > 0)
                {
                    // No trim: bumping an existing row cannot grow the set past the cap.
                    return new RecordEventViewResponse { EventId = eventId, Recorded = true, ViewedAtUtc = now };
                }

                var entry = new RecentlyViewedEvent { UserId = userId, EventId = eventId, ViewedAt = now };
                _db.RecentlyViewedEvents.Add(entry);

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Unique (UserId, EventId) caught a concurrent first view of the same event -
                    // the user has it open in two tabs. Bump the winning row instead; still no
                    // trim, because the set did not grow.
                    _db.Entry(entry).State = EntityState.Detached;

                    await _db.RecentlyViewedEvents
                        .Where(v => v.UserId == userId && v.EventId == eventId)
                        .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewedAt, now));

                    return new RecordEventViewResponse { EventId = eventId, Recorded = true, ViewedAtUtc = now };
                }

                // Only an actual insert can push the history over the cap.
                await TrimAsync(userId);

                return new RecordEventViewResponse { EventId = eventId, Recorded = true, ViewedAtUtc = now };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] RecordViewAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<IReadOnlyList<RecentlyViewedEventResponse>> GetMyRecentlyViewedAsync(int userId, string userRole)
        {
            try
            {
                var settings = await GetSettingsAsync(userId);

                // Rows survive an opt-out so the history comes back if the user changes their
                // mind, but nothing is presented while tracking is off.
                if (!settings.Enabled)
                    return [];

                var rows = await _repository.GetRecentAsync(userId, Cutoff(), _options.MaxItemsPerUser);
                if (rows.Count == 0)
                    return [];

                var visible = await _eventsService.GetVisibleEventsByIds(
                    rows.Select(r => r.EventId),
                    userId,
                    userRole);

                var eventsById = visible.ToDictionary(e => e.Id);

                // Dropped, not redacted - the divergence from the pinned list is deliberate. A
                // redacted row would still disclose that an event the user can no longer see
                // exists, and unlike a star there is nothing here they need to act on.
                return rows
                    .Where(r => eventsById.ContainsKey(r.EventId))
                    .Select(r => new RecentlyViewedEventResponse
                    {
                        EventId = r.EventId,
                        ViewedAtUtc = r.ViewedAt,
                        Event = EventMapper.MapToResponse(eventsById[r.EventId])
                    })
                    .ToList();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] GetMyRecentlyViewedAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<bool> RemoveAsync(int eventId, int userId)
        {
            var removed = await RemoveManyAsync([eventId], userId);
            return removed > 0;
        }

        public async Task<int> RemoveManyAsync(IEnumerable<int> eventIds, int userId)
        {
            // Deliberately not gated on visibility, matching the favourites slice: owning the row
            // is enough authority to delete it, and demanding current visibility would strand
            // entries forever once a private event invitation is revoked.
            try
            {
                var ids = eventIds.Distinct().ToList();
                if (ids.Count == 0)
                    return 0;

                // Scoped by UserId as well as the ids, so a caller can only ever reach their own
                // rows however the request was assembled.
                return await _db.RecentlyViewedEvents
                    .Where(v => v.UserId == userId && ids.Contains(v.EventId))
                    .ExecuteDeleteAsync();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] RemoveManyAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<int> ClearAsync(int userId)
        {
            try
            {
                return await _db.RecentlyViewedEvents
                    .Where(v => v.UserId == userId)
                    .ExecuteDeleteAsync();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] ClearAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<RecentlyViewedMergeResultResponse> MergeAsync(
            MergeRecentlyViewedRequest request,
            int userId,
            string userRole)
        {
            try
            {
                var items = (request.Items ?? []).Take(_options.MaxItemsPerUser).ToList();
                var result = new RecentlyViewedMergeResultResponse { Total = items.Count };

                if (items.Count == 0)
                    return result;

                var settings = await GetSettingsAsync(userId);
                if (!settings.Enabled)
                {
                    result.Skipped = items.Count;
                    return result;
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var cutoff = Cutoff();

                // Every timestamp here came from a browser, so treat the clock as hostile: a
                // future timestamp would otherwise pin an entry to the head of the list forever.
                var candidates = items
                    .Select(i => new MergeCandidate(i.EventId, ClampViewedAt(i.ViewedAtUtc, now)))
                    .Where(i => i.ViewedAt >= cutoff)
                    .ToList();

                // One batched visibility check, never per-id errors. Answering "that one is not
                // yours" item by item would turn this into a probe for private events.
                var visible = candidates.Count == 0
                    ? []
                    : await _eventsService.GetVisibleEventsByIds(candidates.Select(c => c.EventId), userId, userRole);

                var visibleIds = visible.Select(e => e.Id).ToHashSet();
                var mergeable = candidates.Where(c => visibleIds.Contains(c.EventId)).ToList();

                result.Skipped = items.Count - mergeable.Count;

                if (mergeable.Count == 0)
                    return result;

                await ApplyMergeAsync(mergeable, userId);

                result.Merged = mergeable.Count;

                // Once for the whole batch rather than per item.
                await TrimAsync(userId);

                return result;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                // Includes a merge that lost the insert race twice over. Failing is the safe
                // direction: the client keeps its local buffer and retries on the next sign-in,
                // whereas a success response would have it discard events that never landed.
                Logger.Error($"[RecentlyViewedService] MergeAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        /// <summary>
        /// Reads the preference straight from the database, deliberately uncached.
        /// <para>
        /// It was cached once, since it is read on every record-view call and changes almost never.
        /// That is the wrong trade for a privacy switch: refresh-ahead runs its factory on a
        /// detached task and writes the result back with a full TTL, so a refresh that sampled the
        /// enabled state just before the user opted out could restore it after the invalidation
        /// and carry on recording for hours. An indexed single-row read, on a path that is already
        /// writing, costs far less than that failure mode.
        /// </para>
        /// </summary>
        public async Task<RecentlyViewedSettingsResponse> GetSettingsAsync(int userId)
        {
            try
            {
                var row = await _repository.GetSettingAsync(userId);

                return row == null
                    ? new RecentlyViewedSettingsResponse { Enabled = true }
                    : new RecentlyViewedSettingsResponse { Enabled = row.Enabled, UpdatedAtUtc = row.UpdatedAt };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] GetSettingsAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<RecentlyViewedSettingsResponse> UpdateSettingsAsync(bool enabled, int userId)
        {
            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;

                var row = await _db.RecentlyViewedSettings.FirstOrDefaultAsync(s => s.UserId == userId);

                if (row == null)
                {
                    row = new RecentlyViewedSetting { UserId = userId, Enabled = enabled, UpdatedAt = now };
                    _db.RecentlyViewedSettings.Add(row);

                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        // Two tabs toggled at once. Re-read and apply to the winning row so the
                        // last write still decides, rather than surfacing a conflict.
                        _db.Entry(row).State = EntityState.Detached;

                        row = await _db.RecentlyViewedSettings.FirstOrDefaultAsync(s => s.UserId == userId)
                            ?? throw new InternalServerErrorException();

                        row.Enabled = enabled;
                        row.UpdatedAt = now;
                        await _db.SaveChangesAsync();
                    }
                }
                else
                {
                    row.Enabled = enabled;
                    row.UpdatedAt = now;
                    await _db.SaveChangesAsync();
                }

                // Switching off stops collection and hides the list, but keeps the rows. Users who
                // want them gone press Clear history, which is a separate, explicit act.
                return new RecentlyViewedSettingsResponse { Enabled = row.Enabled, UpdatedAtUtc = row.UpdatedAt };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[RecentlyViewedService] UpdateSettingsAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private DateTime Cutoff() =>
            _timeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.RetentionDays);

        private static DateTime ClampViewedAt(DateTime viewedAt, DateTime now)
        {
            var utc = viewedAt.Kind == DateTimeKind.Utc ? viewedAt : viewedAt.ToUniversalTime();
            return utc > now ? now : utc;
        }

        /// <summary>
        /// Drops everything past the cap, newest kept.
        /// <para>
        /// Ordering by id after the timestamp matters: two entries recorded in the same tick would
        /// otherwise be trimmed arbitrarily, and the read path - which orders the same way - could
        /// then present a different 50 than the write path decided to keep.
        /// </para>
        /// </summary>
        private async Task TrimAsync(int userId)
        {
            if (_options.MaxItemsPerUser <= 0)
                return;

            var doomed = await _db.RecentlyViewedEvents
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.ViewedAt)
                .ThenByDescending(v => v.Id)
                .Skip(_options.MaxItemsPerUser)
                .Select(v => v.Id)
                .ToListAsync();

            if (doomed.Count == 0)
                return;

            await _db.RecentlyViewedEvents
                .Where(v => doomed.Contains(v.Id))
                .ExecuteDeleteAsync();
        }

        /// <summary>
        /// Upserts the merged rows, retrying once if a concurrent merge wins an insert race.
        /// <para>
        /// The retry is not politeness. <c>SaveChangesAsync</c> commits as one unit, so a unique
        /// violation on a single event rolls back every other insert and timestamp bump in the
        /// batch. Reporting the surviving rows instead of reapplying would tell the client the
        /// sync succeeded, and it would then clear a local buffer still holding events that never
        /// reached the database. On the retry the conflicting row exists, so it takes the update
        /// path and the rest inserts cleanly.
        /// </para>
        /// </summary>
        private async Task ApplyMergeAsync(
            IReadOnlyList<MergeCandidate> mergeable,
            int userId,
            int attemptsRemaining = 1)
        {
            // Re-read on every attempt: after a rollback the tracked entities describe a state the
            // database never took.
            _db.ChangeTracker.Clear();

            var incomingIds = mergeable.Select(c => c.EventId).ToList();

            var existing = await _db.RecentlyViewedEvents
                .Where(v => v.UserId == userId && incomingIds.Contains(v.EventId))
                .ToDictionaryAsync(v => v.EventId);

            foreach (var candidate in mergeable)
            {
                if (existing.TryGetValue(candidate.EventId, out var row))
                {
                    // Keep whichever view actually happened later. A client timestamp must never
                    // be able to drag an entry backwards down the list.
                    if (candidate.ViewedAt > row.ViewedAt)
                        row.ViewedAt = candidate.ViewedAt;

                    continue;
                }

                _db.RecentlyViewedEvents.Add(new RecentlyViewedEvent
                {
                    UserId = userId,
                    EventId = candidate.EventId,
                    ViewedAt = candidate.ViewedAt
                });
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (attemptsRemaining > 0)
            {
                Logger.Warn("[RecentlyViewedService] MergeAsync lost an insert race; reapplying the batch.");
                await ApplyMergeAsync(mergeable, userId, attemptsRemaining - 1);
            }
        }

        /// <summary>One event to fold in, with its timestamp already clamped and vetted.</summary>
        private sealed record MergeCandidate(int EventId, DateTime ViewedAt);
    }
}
