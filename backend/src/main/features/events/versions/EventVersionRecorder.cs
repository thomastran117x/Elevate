using System.Globalization;
using System.Text.Json;

using backend.main.infrastructure.database.core;

namespace backend.main.features.events.versions;

/// <summary>
/// Builds and records <see cref="EventVersion"/> audit rows for a mutated event.
/// <para>
/// Extracted from <c>EventsService</c> so the recurrence series feature writes byte-identical
/// version history for occurrences it touches in bulk. Deliberately <c>static</c> rather than an
/// injected collaborator: <c>EventsServiceHarness</c> constructs <c>EventsService</c> with 14
/// positional arguments, so adding a constructor dependency there would break every test that
/// runs through it.
/// </para>
/// </summary>
internal static class EventVersionRecorder
{
    /// <summary>
    /// Stages an audit row for <paramref name="ev"/> at its <em>current</em> version number.
    /// Callers must have already incremented <c>CurrentVersionNumber</c> and mutated the entity;
    /// the snapshot is taken from the post-mutation state.
    /// </summary>
    internal static void Add(
        AppDatabaseContext db,
        Events ev,
        string actionType,
        int actorUserId,
        string actorRole,
        int? rollbackSourceVersionNumber,
        IReadOnlyList<EventVersionFieldChange> changedFields,
        DateTime createdAt)
    {
        db.EventVersions.Add(new EventVersion
        {
            EventId = ev.Id,
            VersionNumber = ev.CurrentVersionNumber,
            ActionType = actionType,
            SnapshotJson = JsonSerializer.Serialize(BuildSnapshot(ev)),
            ChangedFieldsJson = JsonSerializer.Serialize(changedFields),
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            RollbackSourceVersionNumber = rollbackSourceVersionNumber,
            CreatedAt = createdAt
        });
    }

    internal static EventVersionSnapshot BuildSnapshot(Events ev) => new()
    {
        Name = ev.Name,
        Description = ev.Description,
        Location = ev.Location,
        IsPrivate = ev.isPrivate,
        MaxParticipants = ev.maxParticipants,
        RegisterCost = ev.registerCost,
        WaitlistEnabled = ev.WaitlistEnabled,
        StartTime = ev.StartTime,
        EndTime = ev.EndTime,
        ClubId = ev.ClubId,
        LifecycleState = ev.LifecycleState,
        Category = ev.Category,
        VenueName = ev.VenueName,
        City = ev.City,
        Latitude = ev.Latitude,
        Longitude = ev.Longitude,
        Tags = ev.Tags?.ToList() ?? [],
        SeriesId = ev.SeriesId,
        OccurrenceIndex = ev.OccurrenceIndex,
        SeriesOverridden = ev.SeriesOverridden
    };

    /// <summary>
    /// Restores field values from a snapshot.
    /// <para>
    /// Series <em>membership</em> (<c>SeriesId</c>, <c>OccurrenceIndex</c>) is deliberately NOT
    /// restored, for the same reason <c>LifecycleState</c> isn't: a rollback would otherwise
    /// silently re-attach an occurrence the organizer had explicitly detached, or renumber it into
    /// a slot another occurrence now owns. Membership changes only through the series endpoints.
    /// </para>
    /// <para>
    /// <c>SeriesOverridden</c> <em>is</em> restored, because it describes the content rather than
    /// the membership: it means "this occurrence has been changed away from what the series
    /// generates". Rolling that change back should put the occurrence back in scope for
    /// series-wide updates, otherwise undoing a one-off edit would leave it excluded forever.
    /// </para>
    /// </summary>
    internal static void ApplySnapshot(Events ev, EventVersionSnapshot snapshot)
    {
        ev.Name = snapshot.Name;
        ev.Description = snapshot.Description;
        ev.Location = snapshot.Location;
        ev.isPrivate = snapshot.IsPrivate;
        ev.maxParticipants = snapshot.MaxParticipants;
        ev.registerCost = snapshot.RegisterCost;
        ev.WaitlistEnabled = snapshot.WaitlistEnabled;
        ev.StartTime = snapshot.StartTime;
        ev.EndTime = snapshot.EndTime;
        ev.ClubId = snapshot.ClubId;
        ev.LifecycleState = snapshot.LifecycleState;
        ev.Category = snapshot.Category;
        ev.VenueName = snapshot.VenueName;
        ev.City = snapshot.City;
        ev.Latitude = snapshot.Latitude;
        ev.Longitude = snapshot.Longitude;
        ev.Tags = snapshot.Tags.ToList();
        ev.SeriesOverridden = snapshot.SeriesOverridden;
    }

    internal static List<EventVersionFieldChange> BuildChangedFields(
        EventVersionSnapshot? previous,
        EventVersionSnapshot current)
    {
        var changes = new List<EventVersionFieldChange>();

        AddChange(changes, "name", previous?.Name, current.Name);
        AddChange(changes, "description", previous?.Description, current.Description);
        AddChange(changes, "location", previous?.Location, current.Location);
        AddChange(changes, "isPrivate", previous?.IsPrivate, current.IsPrivate);
        AddChange(changes, "maxParticipants", previous?.MaxParticipants, current.MaxParticipants);
        AddChange(changes, "registerCost", previous?.RegisterCost, current.RegisterCost);
        AddChange(changes, "waitlistEnabled", previous?.WaitlistEnabled, current.WaitlistEnabled);
        AddChange(changes, "startTime", previous?.StartTime, current.StartTime);
        AddChange(changes, "endTime", previous?.EndTime, current.EndTime);
        AddChange(changes, "clubId", previous?.ClubId, current.ClubId);
        AddChange(changes, "lifecycleState", previous?.LifecycleState, current.LifecycleState);
        AddChange(changes, "category", previous?.Category, current.Category);
        AddChange(changes, "venueName", previous?.VenueName, current.VenueName);
        AddChange(changes, "city", previous?.City, current.City);
        AddChange(changes, "latitude", previous?.Latitude, current.Latitude);
        AddChange(changes, "longitude", previous?.Longitude, current.Longitude);
        AddChange(changes, "tags", previous?.Tags, current.Tags);
        AddChange(changes, "seriesId", previous?.SeriesId, current.SeriesId);
        AddChange(changes, "occurrenceIndex", previous?.OccurrenceIndex, current.OccurrenceIndex);
        AddChange(changes, "seriesOverridden", previous?.SeriesOverridden, current.SeriesOverridden);

        return changes;
    }

    internal static string NormalizeActorRole(string actorRole) =>
        string.IsNullOrWhiteSpace(actorRole) ? "Unknown" : actorRole.Trim();

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        bool? oldValue,
        bool newValue)
    {
        if (oldValue.HasValue && oldValue.Value == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString().ToLowerInvariant(),
            NewValue = newValue.ToString().ToLowerInvariant()
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        int? oldValue,
        int newValue)
    {
        if (oldValue.HasValue && oldValue.Value == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue.ToString(CultureInfo.InvariantCulture)
        });
    }

    /// <summary>
    /// Nullable-to-nullable int overload, for fields like <c>SeriesId</c> where clearing the
    /// value (detaching an occurrence) is itself a meaningful change worth recording.
    /// </summary>
    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        int? oldValue,
        int? newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue?.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        DateTime? oldValue,
        DateTime? newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString("O", CultureInfo.InvariantCulture),
            NewValue = newValue?.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        EventLifecycleState? oldValue,
        EventLifecycleState newValue)
    {
        if (oldValue.HasValue && oldValue.Value == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(),
            NewValue = newValue.ToString()
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        EventCategory? oldValue,
        EventCategory newValue)
    {
        if (oldValue.HasValue && oldValue.Value == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(),
            NewValue = newValue.ToString()
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        double? oldValue,
        double? newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue?.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static void AddChange(
        ICollection<EventVersionFieldChange> changes,
        string field,
        IReadOnlyList<string>? oldValue,
        IReadOnlyList<string> newValue)
    {
        var normalizedOld = oldValue?.ToList() ?? [];
        var normalizedNew = newValue.ToList();

        if (normalizedOld.SequenceEqual(normalizedNew, StringComparer.Ordinal))
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = JsonSerializer.Serialize(normalizedOld),
            NewValue = JsonSerializer.Serialize(normalizedNew)
        });
    }
}
