using System.Globalization;
using System.Text.RegularExpressions;

using backend.main.features.events.versions;

namespace backend.main.features.events;

public static class EventLifecyclePolicy
{
    private static readonly Regex TagPattern = new("^[a-zA-Z0-9-]+$", RegexOptions.Compiled);

    public static bool CanTransition(EventLifecycleState from, EventLifecycleState to) =>
        (from, to) switch
        {
            (EventLifecycleState.Draft, EventLifecycleState.Published) => true,

            // Pausing withdraws an event without ending it; resuming puts it back on sale.
            (EventLifecycleState.Published, EventLifecycleState.Paused) => true,
            (EventLifecycleState.Paused, EventLifecycleState.Published) => true,

            (EventLifecycleState.Published, EventLifecycleState.Cancelled) => true,
            (EventLifecycleState.Paused, EventLifecycleState.Cancelled) => true,

            // Cancelling used to be a dead end. Reinstating re-runs the publish checks, so an
            // event whose start time has since passed cannot sneak back into listings.
            (EventLifecycleState.Cancelled, EventLifecycleState.Published) => true,

            (EventLifecycleState.Published, EventLifecycleState.Archived) => true,
            (EventLifecycleState.Paused, EventLifecycleState.Archived) => true,
            (EventLifecycleState.Cancelled, EventLifecycleState.Archived) => true,

            // Unarchiving lands in Paused rather than Published on purpose: recovering an event
            // should never silently re-expose it to the public or reopen registration.
            (EventLifecycleState.Archived, EventLifecycleState.Paused) => true,

            _ => false,
        };

    public static bool IsVisibleInPublicListings(EventLifecycleState lifecycleState) =>
        lifecycleState == EventLifecycleState.Published;

    /// <summary>
    /// Paused events keep their detail page so people who already registered do not lose the
    /// link; the page itself explains that registration is on hold.
    /// </summary>
    public static bool IsVisibleInPublicDetail(EventLifecycleState lifecycleState) =>
        lifecycleState is EventLifecycleState.Published
            or EventLifecycleState.Cancelled
            or EventLifecycleState.Paused;

    public static bool AllowsRegistration(EventLifecycleState lifecycleState) =>
        lifecycleState == EventLifecycleState.Published;

    /// <summary>
    /// Cancelled and archived events are frozen. Paused events stay editable — reworking an
    /// event while it is off sale is the main reason to pause one.
    /// </summary>
    public static bool AllowsEditing(EventLifecycleState lifecycleState) =>
        lifecycleState is not (EventLifecycleState.Cancelled or EventLifecycleState.Archived);

    /// <summary>
    /// Hard deletion is limited to states that cannot have a live audience: a draft that was
    /// never published, or an event already archived out of sight. Anything else must be
    /// archived first, which is reversible.
    /// </summary>
    public static bool AllowsHardDelete(EventLifecycleState lifecycleState) =>
        lifecycleState is EventLifecycleState.Draft or EventLifecycleState.Archived;

    /// <summary>
    /// Moves an event to <paramref name="target"/> and records the bookkeeping that makes the
    /// change undoable.
    /// <para>
    /// Every path that changes <see cref="Events.LifecycleState"/> must go through here,
    /// including the bulk series operations. Setting the state directly leaves the previous
    /// state pointing at a stale value, so the editor would offer an Undo that restores the
    /// wrong state — e.g. putting a cancelled occurrence back on sale as Published when it was
    /// actually Paused immediately before.
    /// </para>
    /// </summary>
    public static void ApplyTransition(Events ev, EventLifecycleState target, DateTime now)
    {
        ev.PreviousLifecycleState = ev.LifecycleState;
        ev.LifecycleChangedAt = now;
        ev.LifecycleState = target;
    }

    /// <summary>
    /// Deadline for undoing the most recent lifecycle change, or null when there is nothing to
    /// undo or the window has already lapsed.
    /// </summary>
    public static DateTime? GetRevertAvailableUntil(Events ev, DateTime utcNow, int windowHours)
    {
        if (ev.PreviousLifecycleState is null || ev.LifecycleChangedAt is null)
            return null;

        var expiresAt = ev.LifecycleChangedAt.Value.AddHours(windowHours);
        return expiresAt > utcNow ? expiresAt : null;
    }

    public static bool AllowsInvitations(Events ev) =>
        ev.LifecycleState == EventLifecycleState.Published && ev.isPrivate;

    public static EventStatus? ResolveStatus(Events ev, DateTime utcNow)
    {
        if (!ev.StartTime.HasValue)
            return null;

        if (ev.StartTime.Value > utcNow)
            return EventStatus.Upcoming;

        if (!ev.EndTime.HasValue || ev.EndTime.Value > utcNow)
            return EventStatus.Ongoing;

        return EventStatus.Closed;
    }

    /// <summary>
    /// Every move the organizer may make from the event's current state, with the consequences
    /// spelled out against this event's real numbers.
    /// <para>
    /// The client renders its buttons and confirmation prompts straight from this list rather
    /// than reimplementing <see cref="CanTransition"/>, so a future state needs no UI change.
    /// </para>
    /// </summary>
    public static IReadOnlyList<EventLifecycleTransition> GetAvailableTransitions(
        Events ev,
        DateTime utcNow) =>
        ev.LifecycleState switch
        {
            EventLifecycleState.Draft => [Block(Publish(ev), GetPublishIssues(ev, utcNow))],
            EventLifecycleState.Published => [Pause(ev), Cancel(ev), Archive(ev)],
            EventLifecycleState.Paused =>
                [Block(Resume(ev), GetRestoreIssues(ev)), Cancel(ev), Archive(ev)],
            EventLifecycleState.Cancelled =>
                [Block(Reinstate(ev), GetRestoreIssues(ev)), Archive(ev)],
            EventLifecycleState.Archived => [Unarchive(ev)],
            _ => [],
        };

    /// <summary>
    /// Attaches the first outstanding issue to a transition, so the client can disable the
    /// button and say why without knowing which rules apply to which move.
    /// </summary>
    private static EventLifecycleTransition Block(
        EventLifecycleTransition transition,
        IReadOnlyList<string> issues) =>
        issues.Count == 0 ? transition : transition with
        {
            BlockedReason = issues[0]
        };

    private static EventLifecycleTransition Publish(Events ev) => new(
        EventVersionActions.Publish,
        EventLifecycleState.Published,
        "Publish event",
        "Publish this event?",
        IsReversible: true,
        "Reversible — you can pause or undo this afterwards.",
        IsDestructive: false,
        [
            ev.isPrivate
                ? "Only people you invite will be able to see it."
                : "It becomes visible publicly and appears in search.",
            "Registration opens immediately.",
        ]);

    private static EventLifecycleTransition Pause(Events ev) => new(
        EventVersionActions.Pause,
        EventLifecycleState.Paused,
        "Pause event",
        "Pause this event?",
        IsReversible: true,
        "Reversible — resume any time to put it back on sale.",
        IsDestructive: false,
        [
            "It is removed from public search and listings.",
            "New registrations are blocked while it is paused.",
            .. Audience(ev, "They keep their place and are not notified."),
            "The event stays editable, and its page still works for people who already registered.",
        ]);

    private static EventLifecycleTransition Resume(Events ev) => new(
        EventVersionActions.Resume,
        EventLifecycleState.Published,
        "Resume event",
        "Resume this event?",
        IsReversible: true,
        "Reversible — you can pause it again at any time.",
        IsDestructive: false,
        [
            ev.isPrivate
                ? "It becomes visible again to people you invited."
                : "It returns to public search and listings.",
            "Registration reopens.",
        ]);

    private static EventLifecycleTransition Cancel(Events ev) => new(
        EventVersionActions.Cancel,
        EventLifecycleState.Cancelled,
        "Cancel event",
        "Cancel this event?",
        IsReversible: true,
        "Reversible — you can reinstate this event afterwards.",
        IsDestructive: true,
        [
            .. Audience(ev, "Their registrations stay on record, but the event page will show as cancelled."),
            "It is removed from public search and listings.",
            "New registrations are blocked.",
            "The event can no longer be edited until it is reinstated.",
            "If you only need to take it off sale for a while, pause it instead.",
        ]);

    private static EventLifecycleTransition Reinstate(Events ev) => new(
        EventVersionActions.Reinstate,
        EventLifecycleState.Published,
        "Reinstate event",
        "Reinstate this event?",
        IsReversible: true,
        "Reversible — you can cancel or pause it again afterwards.",
        IsDestructive: false,
        [
            "It returns to public listings and no longer shows as cancelled.",
            "Registration reopens.",
            "The event becomes editable again.",
        ]);

    private static EventLifecycleTransition Archive(Events ev) => new(
        EventVersionActions.Archive,
        EventLifecycleState.Archived,
        "Archive event",
        "Archive this event?",
        IsReversible: true,
        "Reversible — you can unarchive this event afterwards.",
        IsDestructive: true,
        [
            "It is hidden from everyone except club managers.",
            "The event can no longer be edited until it is unarchived.",
            .. Audience(ev, "Their registrations are kept for your records."),
        ]);

    private static EventLifecycleTransition Unarchive(Events ev) => new(
        EventVersionActions.Unarchive,
        EventLifecycleState.Paused,
        "Unarchive event",
        "Unarchive this event?",
        IsReversible: true,
        "Reversible — you can archive it again at any time.",
        IsDestructive: false,
        [
            "It moves back to Paused so you can review it before it goes live again.",
            "It stays hidden from public listings until you resume it.",
        ]);

    /// <summary>
    /// One line naming the people already attached to the event, or nothing at all when there
    /// are none — an empty event should not be padded with reassurances about nobody.
    /// </summary>
    private static string[] Audience(Events ev, string consequence)
    {
        var parts = new List<string>();

        if (ev.RegistrationCount > 0)
            parts.Add(Pluralize(ev.RegistrationCount, "person is", "people are") + " registered");

        if (ev.WaitlistCount > 0)
            parts.Add(Pluralize(ev.WaitlistCount, "person is", "people are") + " on the waitlist");

        return parts.Count == 0
            ? []
            : [$"{string.Join(" and ", parts)}. {consequence}"];
    }

    private static string Pluralize(int count, string singular, string plural) =>
        $"{count.ToString("N0", CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    /// <summary>
    /// Everything that must hold before an event may go live for the first time: the content
    /// rules, plus the requirement that it has not already started.
    /// </summary>
    public static List<string> GetPublishIssues(Events ev, DateTime utcNow)
    {
        var issues = GetRestoreIssues(ev);

        if (!ev.StartTime.HasValue)
            issues.Add("Start time is required.");
        else if (ev.StartTime.Value < utcNow)
            issues.Add("Start time must be in the future.");

        return issues;
    }

    /// <summary>
    /// The content rules only, for returning an event to <c>Published</c> from a state it was
    /// already live in — resume, reinstate, and undo.
    /// <para>
    /// Deliberately omits "start time must be in the future". That rule stops an organizer
    /// listing something already over as if it were upcoming; it must not stop them taking back
    /// a cancellation. An event can be paused or cancelled while it is running, so applying the
    /// first-publication rule here would make the reversibility this promises unusable for
    /// exactly the events most likely to be cancelled by mistake. Nothing is lost by allowing
    /// it: registration is closed on the start time independently of lifecycle state
    /// (see EventRegistrationService), so a restored past event is simply visible again and
    /// resolves to Ongoing or Closed.
    /// </para>
    /// </summary>
    public static List<string> GetRestoreIssues(Events ev)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(ev.Name) || ev.Name.Trim().Length is < 3 or > 30)
            issues.Add("Name must be between 3 and 30 characters.");

        if (string.IsNullOrWhiteSpace(ev.Description) || ev.Description.Trim().Length is < 10 or > 200)
            issues.Add("Description must be between 10 and 200 characters.");

        if (string.IsNullOrWhiteSpace(ev.Location) || ev.Location.Trim().Length > 50)
            issues.Add("Location is required and must be 50 characters or fewer.");

        var imageCount = ev.Images.Count;
        if (imageCount is < 1 or > 5)
            issues.Add("At least one image and at most five images are required.");

        if (ev.StartTime.HasValue && ev.EndTime.HasValue && ev.EndTime.Value <= ev.StartTime.Value)
            issues.Add("End time must be later than start time.");

        if (ev.maxParticipants is < 1 or > 10_000)
            issues.Add("Max participants must be between 1 and 10,000.");

        if (ev.registerCost is < 0 or > 50_000)
            issues.Add("Register cost must be between $0 and $50,000.");

        if (ev.WaitlistEnabled && ev.maxParticipants <= 0)
            issues.Add("Waitlists require a capacity limit.");

        if (ev.WaitlistEnabled && ev.registerCost > 0)
            issues.Add("Waitlists are not available for paid events.");

        if (ev.isPrivate && ev.registerCost > 0)
            issues.Add("Private events cannot require a registration fee.");

        if (ev.Latitude.HasValue != ev.Longitude.HasValue)
            issues.Add("Latitude and longitude must both be provided, or both omitted.");

        if (!string.IsNullOrWhiteSpace(ev.VenueName) && ev.VenueName.Length > 100)
            issues.Add("Venue name must be 100 characters or fewer.");

        if (!string.IsNullOrWhiteSpace(ev.City) && ev.City.Length > 100)
            issues.Add("City must be 100 characters or fewer.");

        if (ev.Tags.Count > 10)
            issues.Add("A maximum of 10 tags are allowed.");

        foreach (var tag in ev.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag.Length > 30 || !TagPattern.IsMatch(tag))
            {
                issues.Add($"Tag '{tag}' is invalid. Tags must be 1-30 chars, alphanumeric or dashes.");
            }
        }

        return issues;
    }
}
