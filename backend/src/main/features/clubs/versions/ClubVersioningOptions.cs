namespace backend.main.features.clubs.versions;

public sealed class ClubVersioningOptions
{
    public int RollbackWindowDays { get; set; } = 90;
    public int PurgeBatchSize { get; set; } = 100;
    public bool PurgeEnabled { get; set; } = true;
}
