namespace backend.main.features.events.versions;

public sealed class EventVersioningOptions
{
    public int RollbackWindowDays { get; set; } = 90;
}
