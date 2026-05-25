using System.Text.RegularExpressions;

namespace backend.main.features.events;

public static class EventLifecyclePolicy
{
    private static readonly Regex TagPattern = new("^[a-zA-Z0-9-]+$", RegexOptions.Compiled);

    public static bool CanTransition(EventLifecycleState from, EventLifecycleState to) =>
        (from, to) switch
        {
            (EventLifecycleState.Draft, EventLifecycleState.Published) => true,
            (EventLifecycleState.Published, EventLifecycleState.Cancelled) => true,
            (EventLifecycleState.Published, EventLifecycleState.Archived) => true,
            (EventLifecycleState.Cancelled, EventLifecycleState.Archived) => true,
            _ => false,
        };

    public static bool IsVisibleInPublicListings(EventLifecycleState lifecycleState) =>
        lifecycleState == EventLifecycleState.Published;

    public static bool IsVisibleInPublicDetail(EventLifecycleState lifecycleState) =>
        lifecycleState is EventLifecycleState.Published or EventLifecycleState.Cancelled;

    public static bool AllowsRegistration(EventLifecycleState lifecycleState) =>
        lifecycleState == EventLifecycleState.Published;

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

    public static List<string> GetPublishIssues(Events ev, DateTime utcNow)
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

        if (!ev.StartTime.HasValue)
            issues.Add("Start time is required.");
        else if (ev.StartTime.Value < utcNow)
            issues.Add("Start time must be in the future.");

        if (ev.StartTime.HasValue && ev.EndTime.HasValue && ev.EndTime.Value <= ev.StartTime.Value)
            issues.Add("End time must be later than start time.");

        if (ev.maxParticipants is < 1 or > 10_000)
            issues.Add("Max participants must be between 1 and 10,000.");

        if (ev.registerCost is < 0 or > 50_000)
            issues.Add("Register cost must be between $0 and $50,000.");

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
