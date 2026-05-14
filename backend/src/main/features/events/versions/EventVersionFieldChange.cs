namespace backend.main.features.events.versions;

public sealed class EventVersionFieldChange
{
    public required string Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
