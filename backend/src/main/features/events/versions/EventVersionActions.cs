namespace backend.main.features.events.versions;

public static class EventVersionActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Publish = "publish";
    public const string Cancel = "cancel";
    public const string Archive = "archive";
    public const string Rollback = "rollback";

    /// <summary>The occurrence was materialized as part of a recurrence series.</summary>
    public const string SeriesCreate = "series-create";

    /// <summary>The occurrence was changed by an "update all future occurrences" action.</summary>
    public const string SeriesUpdate = "series-update";

    /// <summary>The occurrence was cancelled as part of cancelling its series.</summary>
    public const string SeriesCancel = "series-cancel";

    /// <summary>The occurrence was detached and is now a standalone event.</summary>
    public const string SeriesDetach = "series-detach";
}
