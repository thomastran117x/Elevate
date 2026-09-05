namespace backend.main.features.events.versions;

public static class EventVersionActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Publish = "publish";
    public const string Cancel = "cancel";
    public const string Archive = "archive";
    public const string Rollback = "rollback";

    /// <summary>Withdrawn from public listings without ending the event. Reversed by <see cref="Resume"/>.</summary>
    public const string Pause = "pause";

    /// <summary>Put back on sale after a pause.</summary>
    public const string Resume = "resume";

    /// <summary>A cancellation was taken back and the event is live again.</summary>
    public const string Reinstate = "reinstate";

    /// <summary>Recovered from the archive, landing in <c>Paused</c> for review.</summary>
    public const string Unarchive = "unarchive";

    /// <summary>The most recent lifecycle change was undone inside the revert window.</summary>
    public const string LifecycleRevert = "lifecycle-revert";

    /// <summary>The occurrence was materialized as part of a recurrence series.</summary>
    public const string SeriesCreate = "series-create";

    /// <summary>The occurrence was changed by an "update all future occurrences" action.</summary>
    public const string SeriesUpdate = "series-update";

    /// <summary>The occurrence was cancelled as part of cancelling its series.</summary>
    public const string SeriesCancel = "series-cancel";

    /// <summary>The occurrence was detached and is now a standalone event.</summary>
    public const string SeriesDetach = "series-detach";
}
