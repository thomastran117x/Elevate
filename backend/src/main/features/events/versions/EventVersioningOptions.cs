namespace backend.main.features.events.versions;

public sealed class EventVersioningOptions
{
    public int RollbackWindowDays { get; set; } = 90;

    /// <summary>
    /// How long after a lifecycle change the organizer may undo it in one click. Deliberately
    /// much shorter than <see cref="RollbackWindowDays"/>: this is a safety net for a misclick,
    /// not a way to flip an event's public state days later.
    /// </summary>
    public int LifecycleRevertWindowHours { get; set; } = 24;
}
