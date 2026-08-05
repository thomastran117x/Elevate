using System.Globalization;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events.images;
using backend.main.features.events.search;
using backend.main.features.events.series.contracts.requests;
using backend.main.features.events.series.contracts.responses;
using backend.main.features.events.versions;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.series;

/// <summary>
/// Materializes recurrence rules into ordinary <see cref="Events"/> rows and operates on them
/// as a group.
/// <para>
/// Every write follows the same contract the rest of the events feature uses — one transaction,
/// a version record per touched occurrence, an Elasticsearch outbox row, then cache invalidation
/// — via <see cref="EventVersionRecorder"/> and <see cref="EventCacheKeys"/>, so occurrences are
/// indistinguishable from hand-created events on every read path.
/// </para>
/// </summary>
public class EventSeriesService : IEventSeriesService
{
    private readonly AppDatabaseContext _db;
    private readonly IEventSeriesRepository _seriesRepository;
    private readonly IEventsRepository _eventsRepository;
    private readonly IEventImageRepository _imageRepository;
    private readonly IClubService _clubService;
    private readonly IAzureBlobService _blobService;
    private readonly ICacheService _cache;
    private readonly IRefreshAheadCache _refreshCache;
    private readonly IEventSearchOutboxWriter _outboxWriter;
    private readonly TimeProvider _timeProvider;

    public EventSeriesService(
        AppDatabaseContext db,
        IEventSeriesRepository seriesRepository,
        IEventsRepository eventsRepository,
        IEventImageRepository imageRepository,
        IClubService clubService,
        IAzureBlobService blobService,
        ICacheService cache,
        IRefreshAheadCache refreshCache,
        IEventSearchOutboxWriter outboxWriter,
        TimeProvider timeProvider)
    {
        _db = db;
        _seriesRepository = seriesRepository;
        _eventsRepository = eventsRepository;
        _imageRepository = imageRepository;
        _clubService = clubService;
        _blobService = blobService;
        _cache = cache;
        _refreshCache = refreshCache;
        _outboxWriter = outboxWriter;
        _timeProvider = timeProvider;
    }

    // ------------------------------------------------------------------ preview

    public async Task<EventSeriesPreviewResponse> PreviewAsync(
        int clubId,
        int userId,
        string userRole,
        EventRecurrenceRuleRequest rule)
    {
        try
        {
            await EnsureCanManageClubAsync(clubId, userId, userRole);

            var expansion = EventRecurrenceExpander.Expand(rule.ToRule());

            return BuildPreviewResponse(rule.TimeZoneId.Trim(), expansion);
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(PreviewAsync));
        }
    }

    // ------------------------------------------------------------------ create

    public async Task<EventSeriesResponse> CreateFromDraftAsync(
        int templateEventId,
        int userId,
        string userRole,
        CreateEventSeriesRequest request)
    {
        try
        {
            var template = await GetTrackedEventAsync(templateEventId);
            await EnsureCanManageClubAsync(template.ClubId, userId, userRole);

            if (template.SeriesId.HasValue)
                throw new ConflictException($"Event {templateEventId} already belongs to a series.");

            if (template.LifecycleState != EventLifecycleState.Draft)
            {
                throw new ConflictException(
                    "Only a draft can be turned into a series. Create a new draft to start one.");
            }

            var rule = request.Recurrence.ToRule();
            var expansion = EventRecurrenceExpander.Expand(rule);
            var timeZoneId = rule.TimeZoneId.Trim();
            var now = GetUtcNow();

            var series = new EventSeries
            {
                ClubId = template.ClubId,
                TemplateEventId = template.Id,
                Frequency = rule.Frequency,
                Interval = rule.Interval,
                ByWeekdayMask = ToWeekdayMask(rule.ByWeekdays),
                MonthlyDayPolicy = rule.MonthlyDayPolicy,
                TimeZoneId = timeZoneId,
                FirstOccurrenceLocalStart = rule.FirstOccurrenceLocalStart,
                DurationMinutes = rule.DurationMinutes,
                EndMode = rule.EndMode,
                EndLocalDate = rule.EndLocalDate?.ToDateTime(TimeOnly.MinValue),
                OccurrenceCount = rule.OccurrenceCount,
                GeneratedCount = expansion.Occurrences.Count,
                Status = EventSeriesStatus.Active,
                CreatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };

            var templateImageUrls = template.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => i.ImageUrl)
                .ToList();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.EventSeries.Add(series);
            await _db.SaveChangesAsync();

            // Occurrence 0 reuses the template itself, so starting a series does not leave an
            // orphaned draft behind in the club's event list.
            var first = expansion.Occurrences[0];
            template.SeriesId = series.Id;
            template.OccurrenceIndex = 0;
            template.TimeZoneId = timeZoneId;
            template.StartTime = first.StartUtc;
            template.EndTime = first.EndUtc;
            template.CurrentVersionNumber += 1;
            template.UpdatedAt = now;

            EventVersionRecorder.Add(
                _db,
                template,
                EventVersionActions.SeriesCreate,
                userId,
                EventVersionRecorder.NormalizeActorRole(userRole),
                rollbackSourceVersionNumber: null,
                changedFields: EventVersionRecorder.BuildChangedFields(null, EventVersionRecorder.BuildSnapshot(template)),
                createdAt: now);

            _outboxWriter.StageSync(template);

            var generated = new List<Events>();

            foreach (var slot in expansion.Occurrences.Skip(1))
            {
                var occurrence = CloneTemplate(template, series.Id, slot, timeZoneId, now);
                generated.Add(occurrence);
            }

            if (generated.Count > 0)
            {
                await _db.Events.AddRangeAsync(generated);
                await _db.SaveChangesAsync();

                foreach (var occurrence in generated)
                {
                    EventVersionRecorder.Add(
                        _db,
                        occurrence,
                        EventVersionActions.SeriesCreate,
                        userId,
                        EventVersionRecorder.NormalizeActorRole(userRole),
                        rollbackSourceVersionNumber: null,
                        changedFields: EventVersionRecorder.BuildChangedFields(
                            null,
                            EventVersionRecorder.BuildSnapshot(occurrence)),
                        createdAt: now);

                    if (templateImageUrls.Count > 0)
                        await _imageRepository.AddImagesAsync(occurrence.Id, templateImageUrls);

                    _outboxWriter.StageSync(occurrence);
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await InvalidateAsync(generated.Select(e => e.Id).Append(template.Id));

            return await BuildSeriesResponseAsync(series, expansion.Warnings);
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(CreateFromDraftAsync));
        }
    }

    // ------------------------------------------------------------------ read

    public async Task<EventSeriesResponse> GetAsync(int seriesId, int userId, string userRole)
    {
        try
        {
            var series = await GetSeriesOrThrowAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            return await BuildSeriesResponseAsync(series, []);
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(GetAsync));
        }
    }

    public async Task<(IReadOnlyList<EventSeriesSummaryResponse> Series, int TotalCount)> GetByClubAsync(
        int clubId,
        int userId,
        string userRole,
        int page,
        int pageSize)
    {
        try
        {
            await EnsureCanManageClubAsync(clubId, userId, userRole);

            var (series, totalCount) = await _seriesRepository.GetByClubAsync(
                clubId,
                page < 1 ? 1 : page,
                Math.Clamp(pageSize, 1, 100));

            var now = GetUtcNow();
            var summaries = new List<EventSeriesSummaryResponse>();

            foreach (var item in series)
            {
                var occurrences = await _seriesRepository.GetOccurrencesAsync(item.Id);

                summaries.Add(new EventSeriesSummaryResponse
                {
                    Id = item.Id,
                    ClubId = item.ClubId,
                    TemplateEventId = item.TemplateEventId,
                    Status = item.Status,
                    GeneratedCount = item.GeneratedCount,
                    Rule = BuildRuleResponse(item),
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt,
                    Name = occurrences.FirstOrDefault()?.Name,
                    NextOccurrenceUtc = occurrences
                        .Where(o => o.StartTime > now)
                        .Select(o => o.StartTime)
                        .FirstOrDefault()
                });
            }

            return (summaries, totalCount);
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(GetByClubAsync));
        }
    }

    // ------------------------------------------------------------------ extend

    public async Task<EventSeriesResponse> ExtendAsync(
        int seriesId,
        int userId,
        string userRole,
        ExtendEventSeriesRequest request)
    {
        try
        {
            var series = await GetTrackedSeriesAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            if (series.Status == EventSeriesStatus.Cancelled)
                throw new ConflictException("A cancelled series cannot be extended.");

            if (request.OccurrenceCount.HasValue)
            {
                series.EndMode = EventRecurrenceEndMode.Count;
                series.OccurrenceCount = request.OccurrenceCount.Value;
                series.EndLocalDate = null;
            }
            else
            {
                series.EndMode = EventRecurrenceEndMode.UntilDate;
                series.EndLocalDate = DateOnly
                    .Parse(request.UntilLocalDate!.Trim(), CultureInfo.InvariantCulture)
                    .ToDateTime(TimeOnly.MinValue);
                series.OccurrenceCount = null;
            }

            var expansion = EventRecurrenceExpander.Expand(ToRule(series));

            if (expansion.Occurrences.Count <= series.GeneratedCount)
            {
                throw new BadRequestException(
                    "That change would not add any occurrences. Choose a later end date or a higher count.");
            }

            var templateSource = await _db.Events
                .Include(e => e.Images)
                .Where(e => e.SeriesId == series.Id)
                .OrderBy(e => e.OccurrenceIndex)
                .FirstOrDefaultAsync()
                ?? throw new ConflictException("This series has no occurrences left to copy from.");

            var templateImageUrls = templateSource.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => i.ImageUrl)
                .ToList();

            var now = GetUtcNow();
            var added = new List<Events>();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            // Resume at the high-water mark rather than re-creating rows that already exist,
            // so extending twice is idempotent for the occurrences already materialized.
            foreach (var slot in expansion.Occurrences.Skip(series.GeneratedCount))
                added.Add(CloneTemplate(templateSource, series.Id, slot, series.TimeZoneId, now));

            await _db.Events.AddRangeAsync(added);
            await _db.SaveChangesAsync();

            foreach (var occurrence in added)
            {
                EventVersionRecorder.Add(
                    _db,
                    occurrence,
                    EventVersionActions.SeriesCreate,
                    userId,
                    EventVersionRecorder.NormalizeActorRole(userRole),
                    rollbackSourceVersionNumber: null,
                    changedFields: EventVersionRecorder.BuildChangedFields(
                        null,
                        EventVersionRecorder.BuildSnapshot(occurrence)),
                    createdAt: now);

                if (templateImageUrls.Count > 0)
                    await _imageRepository.AddImagesAsync(occurrence.Id, templateImageUrls);

                _outboxWriter.StageSync(occurrence);
            }

            series.GeneratedCount = expansion.Occurrences.Count;
            series.UpdatedAt = now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await InvalidateAsync(added.Select(e => e.Id));

            return await BuildSeriesResponseAsync(series, expansion.Warnings);
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(ExtendAsync));
        }
    }

    // ------------------------------------------------------------------ publish

    public async Task<EventSeriesBulkResultResponse> PublishAsync(int seriesId, int userId, string userRole)
    {
        try
        {
            var series = await GetTrackedSeriesAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            var occurrences = await GetTrackedOccurrencesAsync(seriesId);
            var now = GetUtcNow();
            var result = new EventSeriesBulkResultResponse { SeriesId = seriesId };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            foreach (var occurrence in occurrences)
            {
                if (occurrence.LifecycleState != EventLifecycleState.Draft)
                {
                    result.Skipped.Add(new EventSeriesSkippedOccurrence
                    {
                        EventId = occurrence.Id,
                        OccurrenceIndex = occurrence.OccurrenceIndex,
                        Reason = "not-a-draft",
                        Details = [$"This occurrence is already {occurrence.LifecycleState.ToString().ToLowerInvariant()}."]
                    });

                    continue;
                }

                // An occurrence whose start has already passed fails the publish checks. Report
                // it and keep going — one stale draft must not block the rest of the series.
                var issues = EventLifecyclePolicy.GetPublishIssues(occurrence, now);

                if (issues.Count > 0)
                {
                    result.Skipped.Add(new EventSeriesSkippedOccurrence
                    {
                        EventId = occurrence.Id,
                        OccurrenceIndex = occurrence.OccurrenceIndex,
                        Reason = "not-publish-ready",
                        Details = issues
                    });

                    continue;
                }

                occurrence.LifecycleState = EventLifecycleState.Published;
                occurrence.CurrentVersionNumber += 1;
                occurrence.UpdatedAt = now;

                RecordAndStage(occurrence, EventVersionActions.Publish, userId, userRole, now);

                result.AffectedEventIds.Add(occurrence.Id);
            }

            series.UpdatedAt = now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            result.AffectedCount = result.AffectedEventIds.Count;
            await InvalidateAsync(result.AffectedEventIds);

            return result;
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(PublishAsync));
        }
    }

    // ------------------------------------------------------------------ update all future

    public async Task<EventSeriesBulkResultResponse> UpdateFutureOccurrencesAsync(
        int seriesId,
        int userId,
        string userRole,
        UpdateFutureOccurrencesRequest request)
    {
        try
        {
            var series = await GetTrackedSeriesAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            var occurrences = await GetTrackedOccurrencesAsync(seriesId);

            var pivot = occurrences.FirstOrDefault(o => o.Id == request.FromEventId)
                ?? throw new ResourceNotFoundException(
                    $"Event {request.FromEventId} is not an occurrence of series {seriesId}.");

            var timeZone = EventSeriesTimeZones.Resolve(series.TimeZoneId);
            var now = GetUtcNow();
            var result = new EventSeriesBulkResultResponse { SeriesId = seriesId };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            foreach (var occurrence in occurrences)
            {
                if (!IsInFutureScope(occurrence, pivot, now, request.IncludeOverridden, result))
                    continue;

                var previous = EventVersionRecorder.BuildSnapshot(occurrence);

                if (!TryApplyPatch(occurrence, request, timeZone, result, out var retimed))
                    continue;

                occurrence.CurrentVersionNumber += 1;
                occurrence.UpdatedAt = now;

                RecordAndStage(
                    occurrence,
                    EventVersionActions.SeriesUpdate,
                    userId,
                    userRole,
                    now,
                    previous);

                result.AffectedEventIds.Add(occurrence.Id);

                if (retimed && occurrence.RegistrationCount > 0)
                    result.RetimedWithRegistrations.Add(occurrence.Id);
            }

            if (request.ImageUrls is not null)
            {
                foreach (var eventId in result.AffectedEventIds)
                {
                    await _imageRepository.DeleteAllByEventIdAsync(eventId);

                    if (request.ImageUrls.Count > 0)
                        await _imageRepository.AddImagesAsync(eventId, request.ImageUrls.Take(5));
                }
            }

            series.UpdatedAt = now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            result.AffectedCount = result.AffectedEventIds.Count;
            await InvalidateAsync(result.AffectedEventIds);

            return result;
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(UpdateFutureOccurrencesAsync));
        }
    }

    // ------------------------------------------------------------------ cancel

    public async Task<EventSeriesBulkResultResponse> CancelAsync(
        int seriesId,
        int userId,
        string userRole,
        CancelEventSeriesRequest request)
    {
        try
        {
            var series = await GetTrackedSeriesAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            var occurrences = await GetTrackedOccurrencesAsync(seriesId);
            var now = GetUtcNow();
            var result = new EventSeriesBulkResultResponse { SeriesId = seriesId };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            foreach (var occurrence in occurrences)
            {
                if (request.FutureOnly && occurrence.StartTime <= now)
                {
                    result.Skipped.Add(Skip(occurrence, "already-started", "This occurrence has already started."));
                    continue;
                }

                if (!EventLifecyclePolicy.CanTransition(occurrence.LifecycleState, EventLifecycleState.Cancelled))
                {
                    result.Skipped.Add(Skip(
                        occurrence,
                        occurrence.LifecycleState == EventLifecycleState.Draft ? "draft-not-cancellable" : "not-cancellable",
                        $"A {occurrence.LifecycleState.ToString().ToLowerInvariant()} occurrence cannot be cancelled."));

                    continue;
                }

                occurrence.LifecycleState = EventLifecycleState.Cancelled;
                occurrence.CurrentVersionNumber += 1;
                occurrence.UpdatedAt = now;

                RecordAndStage(occurrence, EventVersionActions.SeriesCancel, userId, userRole, now);

                result.AffectedEventIds.Add(occurrence.Id);
            }

            series.Status = EventSeriesStatus.Cancelled;
            series.UpdatedAt = now;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            result.AffectedCount = result.AffectedEventIds.Count;
            await InvalidateAsync(result.AffectedEventIds);

            return result;
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(CancelAsync));
        }
    }

    // ------------------------------------------------------------------ delete

    public async Task<EventSeriesBulkResultResponse> DeleteAsync(
        int seriesId,
        int userId,
        string userRole,
        DeleteEventSeriesRequest request)
    {
        try
        {
            var series = await GetTrackedSeriesAsync(seriesId);
            await EnsureCanManageClubAsync(series.ClubId, userId, userRole);

            var occurrences = await GetTrackedOccurrencesAsync(seriesId);
            var now = GetUtcNow();
            var result = new EventSeriesBulkResultResponse { SeriesId = seriesId };

            var toDelete = new List<Events>();
            var toDetach = new List<Events>();

            foreach (var occurrence in occurrences)
            {
                // An occurrence anyone has registered for is never deleted here, whatever the
                // scope says. Detaching leaves it standing as an ordinary event so attendees
                // keep their registration.
                if (occurrence.RegistrationCount > 0)
                {
                    toDetach.Add(occurrence);
                    result.Skipped.Add(Skip(
                        occurrence,
                        "has-registrations",
                        "Kept as a standalone event because people have registered."));

                    continue;
                }

                var deletable = request.Scope switch
                {
                    EventSeriesDeleteScope.SeriesRecordOnly => false,
                    EventSeriesDeleteScope.FutureDrafts =>
                        occurrence.LifecycleState == EventLifecycleState.Draft && occurrence.StartTime > now,
                    EventSeriesDeleteScope.AllUnregistered => true,
                    _ => false
                };

                if (deletable)
                    toDelete.Add(occurrence);
                else
                    toDetach.Add(occurrence);
            }

            var deletedImageUrls = new List<string>();

            await using var transaction = await _db.Database.BeginTransactionAsync();

            foreach (var occurrence in toDetach)
            {
                occurrence.SeriesId = null;
                occurrence.OccurrenceIndex = null;
                occurrence.CurrentVersionNumber += 1;
                occurrence.UpdatedAt = now;

                RecordAndStage(occurrence, EventVersionActions.SeriesDetach, userId, userRole, now);
            }

            if (toDelete.Count > 0)
            {
                var deleteIds = toDelete.Select(o => o.Id).ToList();

                deletedImageUrls.AddRange(await _db.EventImages
                    .Where(i => deleteIds.Contains(i.EventId))
                    .Select(i => i.ImageUrl)
                    .ToListAsync());

                foreach (var id in deleteIds)
                    _outboxWriter.StageDelete(id);

                _db.Events.RemoveRange(toDelete);
                result.AffectedEventIds.AddRange(deleteIds);
            }

            await _db.SaveChangesAsync();

            _db.EventSeries.Remove(series);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            if (deletedImageUrls.Count > 0)
                _ = Task.WhenAll(deletedImageUrls.Select(url => _blobService.DeleteBlobAsync(url)));

            result.AffectedCount = result.AffectedEventIds.Count;
            await InvalidateAsync(occurrences.Select(o => o.Id));

            return result;
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(DeleteAsync));
        }
    }

    // ------------------------------------------------------------------ detach

    public async Task<Events> DetachOccurrenceAsync(int eventId, int userId, string userRole)
    {
        try
        {
            var occurrence = await GetTrackedEventAsync(eventId);
            await EnsureCanManageClubAsync(occurrence.ClubId, userId, userRole);

            if (!occurrence.SeriesId.HasValue)
                throw new ConflictException($"Event {eventId} does not belong to a series.");

            var now = GetUtcNow();
            var previous = EventVersionRecorder.BuildSnapshot(occurrence);

            occurrence.SeriesId = null;
            occurrence.OccurrenceIndex = null;
            occurrence.SeriesOverridden = false;
            occurrence.CurrentVersionNumber += 1;
            occurrence.UpdatedAt = now;

            await using var transaction = await _db.Database.BeginTransactionAsync();

            RecordAndStage(occurrence, EventVersionActions.SeriesDetach, userId, userRole, now, previous);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await InvalidateAsync([eventId]);

            return occurrence;
        }
        catch (Exception e)
        {
            throw Rethrow(e, nameof(DetachOccurrenceAsync));
        }
    }

    // ------------------------------------------------------------------ scope + patch

    /// <summary>
    /// "Future" means: at or after the pivot's position in the series, AND not yet started.
    /// <para>
    /// Both conditions, not either. The index keeps the boundary deterministic even when the
    /// patch itself changes the UTC instants mid-request; the clock guarantees nothing already
    /// running or finished is rewritten underneath its attendees.
    /// </para>
    /// </summary>
    private static bool IsInFutureScope(
        Events occurrence,
        Events pivot,
        DateTime now,
        bool includeOverridden,
        EventSeriesBulkResultResponse result)
    {
        if (occurrence.OccurrenceIndex < pivot.OccurrenceIndex)
            return false;

        if (occurrence.StartTime <= now)
        {
            if (occurrence.Id == pivot.Id)
                result.Skipped.Add(Skip(occurrence, "already-started", "This occurrence has already started."));

            return false;
        }

        if (occurrence.LifecycleState is EventLifecycleState.Cancelled or EventLifecycleState.Archived)
        {
            result.Skipped.Add(Skip(
                occurrence,
                "not-editable",
                $"A {occurrence.LifecycleState.ToString().ToLowerInvariant()} occurrence cannot be updated."));

            return false;
        }

        if (occurrence.SeriesOverridden && !includeOverridden && occurrence.Id != pivot.Id)
        {
            result.Skipped.Add(Skip(
                occurrence,
                "individually-edited",
                "This occurrence was edited on its own and was left unchanged."));

            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the patch, refusing anything that would invalidate an occurrence people have
    /// already committed to. A refusal skips that occurrence and is reported; it does not abort
    /// the batch, because eleven successful updates beat one all-or-nothing failure.
    /// </summary>
    private static bool TryApplyPatch(
        Events occurrence,
        UpdateFutureOccurrencesRequest request,
        TimeZoneInfo timeZone,
        EventSeriesBulkResultResponse result,
        out bool retimed)
    {
        retimed = false;

        if (request.MaxParticipants.HasValue
            && request.MaxParticipants.Value < occurrence.RegistrationCount)
        {
            result.Skipped.Add(Skip(
                occurrence,
                "capacity-below-registrations",
                $"Capacity of {request.MaxParticipants.Value} is below the {occurrence.RegistrationCount} "
                + "people already registered."));

            return false;
        }

        if (request.RegisterCost.HasValue
            && request.RegisterCost.Value != occurrence.registerCost
            && occurrence.RegistrationCount > 0)
        {
            result.Skipped.Add(Skip(
                occurrence,
                "repricing-with-registrations",
                "The price was left unchanged because people have already paid to attend."));

            return false;
        }

        var maxParticipants = request.MaxParticipants ?? occurrence.maxParticipants;
        var registerCost = request.RegisterCost ?? occurrence.registerCost;
        var waitlistEnabled = request.WaitlistEnabled ?? occurrence.WaitlistEnabled;

        // Validate the merged state, not just the fields in the request: turning a waitlist on
        // without a capacity, or clearing a capacity while one is on, both pass the per-field
        // checks while persisting a combination JoinAsync would reject outright.
        if (waitlistEnabled && maxParticipants <= 0)
        {
            result.Skipped.Add(Skip(occurrence, "waitlist-invalid", "Waitlists require a capacity limit."));
            return false;
        }

        if (waitlistEnabled && registerCost > 0)
        {
            result.Skipped.Add(Skip(occurrence, "waitlist-invalid", "Waitlists are not available for paid events."));
            return false;
        }

        if (request.Name is not null)
            occurrence.Name = request.Name.Trim();

        if (request.Description is not null)
            occurrence.Description = request.Description.Trim();

        if (request.Location is not null)
            occurrence.Location = request.Location.Trim();

        if (request.VenueName is not null)
            occurrence.VenueName = request.VenueName.Trim();

        if (request.City is not null)
            occurrence.City = request.City.Trim();

        if (request.Latitude.HasValue || request.Longitude.HasValue)
        {
            occurrence.Latitude = request.Latitude;
            occurrence.Longitude = request.Longitude;
        }

        if (request.Category.HasValue)
            occurrence.Category = request.Category.Value;

        if (request.Tags is not null)
            occurrence.Tags = EventTagNormalizer.Normalize(request.Tags);

        if (request.IsPrivate.HasValue)
            occurrence.isPrivate = request.IsPrivate.Value;

        occurrence.maxParticipants = maxParticipants;
        occurrence.registerCost = registerCost;
        occurrence.WaitlistEnabled = waitlistEnabled;

        // Retiming is expressed as a wall-clock time in the series' zone and re-converted per
        // occurrence, so the shifted series survives DST exactly as the original generation did.
        if (request.TryParseLocalStartTime(out var localStartTime) && occurrence.StartTime.HasValue)
        {
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(occurrence.StartTime.Value, DateTimeKind.Utc),
                timeZone).Date;

            var newLocalStart = localDate.Add(localStartTime.ToTimeSpan());
            var resolved = ResolveLocalToUtc(newLocalStart, timeZone);

            occurrence.StartTime = resolved;
            retimed = true;
        }

        var duration = request.DurationMinutes;

        if (duration.HasValue && occurrence.StartTime.HasValue)
            occurrence.EndTime = occurrence.StartTime.Value.AddMinutes(duration.Value);
        else if (retimed && occurrence.EndTime.HasValue && occurrence.StartTime.HasValue)
            occurrence.EndTime = occurrence.StartTime.Value.AddMinutes(60);

        return true;
    }

    /// <summary>
    /// Wall clock to UTC for a retime, resolving DST gaps and overlaps the same way the expander
    /// does so a shifted occurrence agrees with a freshly generated one.
    /// </summary>
    private static DateTime ResolveLocalToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var resolved = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(resolved))
        {
            var gap = timeZone.GetUtcOffset(resolved.AddDays(1)) - timeZone.GetUtcOffset(resolved.AddDays(-1));

            if (gap > TimeSpan.Zero)
                resolved += gap;

            for (var attempt = 0; attempt < 16 && timeZone.IsInvalidTime(resolved); attempt++)
                resolved = resolved.AddMinutes(15);
        }

        if (timeZone.IsAmbiguousTime(resolved))
        {
            var earliest = timeZone.GetAmbiguousTimeOffsets(resolved).Max();
            return DateTime.SpecifyKind(resolved - earliest, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(resolved, timeZone);
    }

    // ------------------------------------------------------------------ helpers

    private Events CloneTemplate(
        Events template,
        int seriesId,
        EventOccurrenceSlot slot,
        string timeZoneId,
        DateTime now) => new()
        {
            Name = template.Name,
            Description = template.Description,
            Location = template.Location,
            isPrivate = template.isPrivate,
            maxParticipants = template.maxParticipants,
            registerCost = template.registerCost,
            WaitlistEnabled = template.WaitlistEnabled,
            StartTime = slot.StartUtc,
            EndTime = slot.EndUtc,
            ClubId = template.ClubId,
            LifecycleState = EventLifecycleState.Draft,
            Category = template.Category,
            VenueName = template.VenueName,
            City = template.City,
            Latitude = template.Latitude,
            Longitude = template.Longitude,
            Tags = template.Tags?.ToList() ?? [],
            SeriesId = seriesId,
            OccurrenceIndex = slot.Index,
            TimeZoneId = timeZoneId,
            CurrentVersionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

    private void RecordAndStage(
        Events occurrence,
        string action,
        int userId,
        string userRole,
        DateTime now,
        EventVersionSnapshot? previous = null)
    {
        EventVersionRecorder.Add(
            _db,
            occurrence,
            action,
            userId,
            EventVersionRecorder.NormalizeActorRole(userRole),
            rollbackSourceVersionNumber: null,
            changedFields: EventVersionRecorder.BuildChangedFields(
                previous,
                EventVersionRecorder.BuildSnapshot(occurrence)),
            createdAt: now);

        _outboxWriter.StageSync(occurrence);
    }

    private static EventSeriesSkippedOccurrence Skip(Events occurrence, string reason, string detail) => new()
    {
        EventId = occurrence.Id,
        OccurrenceIndex = occurrence.OccurrenceIndex,
        Reason = reason,
        Details = [detail]
    };

    private async Task InvalidateAsync(IEnumerable<int> eventIds)
    {
        var ids = eventIds.Distinct().ToList();

        if (ids.Count > 0)
            await Task.WhenAll(ids.Select(id => _refreshCache.RemoveAsync(EventCacheKeys.Event(id))));

        await _cache.IncrementAsync(EventCacheKeys.ListVersion);
    }

    private async Task<EventSeries> GetSeriesOrThrowAsync(int seriesId) =>
        await _seriesRepository.GetByIdAsync(seriesId)
        ?? throw new ResourceNotFoundException($"Series {seriesId} not found");

    private async Task<EventSeries> GetTrackedSeriesAsync(int seriesId) =>
        await _db.EventSeries.FirstOrDefaultAsync(s => s.Id == seriesId)
        ?? throw new ResourceNotFoundException($"Series {seriesId} not found");

    private async Task<Events> GetTrackedEventAsync(int eventId) =>
        await _db.Events.Include(e => e.Images).FirstOrDefaultAsync(e => e.Id == eventId)
        ?? throw new ResourceNotFoundException($"Event {eventId} not found");

    private async Task<List<Events>> GetTrackedOccurrencesAsync(int seriesId) =>
        await _db.Events
            .Include(e => e.Images)
            .Where(e => e.SeriesId == seriesId)
            .OrderBy(e => e.OccurrenceIndex)
            .ToListAsync();

    private async Task EnsureCanManageClubAsync(int clubId, int userId, string userRole)
    {
        var club = await _clubService.GetClub(clubId);

        if (!await _clubService.CanManageClubAsync(club.Id, userId, userRole))
            throw new ForbiddenException("Not allowed");
    }

    private DateTime GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static int ToWeekdayMask(IReadOnlyList<DayOfWeek>? weekdays)
    {
        if (weekdays is null || weekdays.Count == 0)
            return 0;

        return weekdays.Distinct().Aggregate(0, (mask, day) => mask | (1 << (int)day));
    }

    private static List<DayOfWeek> FromWeekdayMask(int mask)
    {
        if (mask == 0)
            return [];

        return Enum.GetValues<DayOfWeek>()
            .Where(day => (mask & (1 << (int)day)) != 0)
            .ToList();
    }

    private static EventRecurrenceRule ToRule(EventSeries series) => new(
        series.Frequency,
        series.Interval,
        DateTime.SpecifyKind(series.FirstOccurrenceLocalStart, DateTimeKind.Unspecified),
        series.DurationMinutes,
        FromWeekdayMask(series.ByWeekdayMask),
        series.MonthlyDayPolicy,
        series.EndMode,
        series.EndLocalDate.HasValue ? DateOnly.FromDateTime(series.EndLocalDate.Value) : null,
        series.OccurrenceCount,
        series.TimeZoneId);

    private static EventSeriesRuleResponse BuildRuleResponse(EventSeries series) => new()
    {
        Frequency = series.Frequency,
        Interval = series.Interval,
        ByWeekdays = FromWeekdayMask(series.ByWeekdayMask),
        MonthlyDayPolicy = series.MonthlyDayPolicy,
        TimeZoneId = series.TimeZoneId,
        FirstOccurrenceLocalStart = series.FirstOccurrenceLocalStart
            .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DurationMinutes = series.DurationMinutes,
        EndMode = series.EndMode,
        EndLocalDate = series.EndLocalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        OccurrenceCount = series.OccurrenceCount
    };

    private static EventSeriesPreviewResponse BuildPreviewResponse(
        string timeZoneId,
        EventRecurrenceExpansion expansion)
    {
        var timeZone = EventSeriesTimeZones.Resolve(timeZoneId);

        return new EventSeriesPreviewResponse
        {
            TimeZoneId = timeZoneId,
            OccurrenceCount = expansion.Occurrences.Count,
            Warnings = expansion.Warnings.ToList(),
            Occurrences = expansion.Occurrences
                .Select(slot => new EventOccurrencePreviewResponse
                {
                    Index = slot.Index,
                    LocalStart = slot.LocalStart.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                    StartTimeUtc = slot.StartUtc,
                    EndTimeUtc = slot.EndUtc,
                    UtcOffset = FormatOffset(timeZone.GetUtcOffset(slot.StartUtc)),
                    WasInvalidLocalTime = slot.LocalStartWasInvalid,
                    WasAmbiguousLocalTime = slot.LocalStartWasAmbiguous
                })
                .ToList()
        };
    }

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+")
        + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private async Task<EventSeriesResponse> BuildSeriesResponseAsync(
        EventSeries series,
        IReadOnlyList<string> warnings)
    {
        var occurrences = await _seriesRepository.GetOccurrencesAsync(series.Id);
        var now = GetUtcNow();

        return new EventSeriesResponse
        {
            Id = series.Id,
            ClubId = series.ClubId,
            TemplateEventId = series.TemplateEventId,
            Status = series.Status,
            GeneratedCount = series.GeneratedCount,
            Rule = BuildRuleResponse(series),
            CreatedAt = series.CreatedAt,
            UpdatedAt = series.UpdatedAt,
            Warnings = warnings.ToList(),
            Occurrences = occurrences
                .Select(o => EventMapper.MapToManagedResponse(o, EventLifecyclePolicy.GetPublishIssues(o, now)))
                .ToList()
        };
    }

    private static Exception Rethrow(Exception e, string operation)
    {
        if (e is AppException)
            return e;

        Logger.Error($"[EventSeriesService] {operation} failed: {e}");

        return new InternalServerErrorException();
    }
}
