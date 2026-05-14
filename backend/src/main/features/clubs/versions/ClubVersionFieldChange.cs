namespace backend.main.features.clubs.versions;

public sealed class ClubVersionFieldChange
{
    public required string Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
