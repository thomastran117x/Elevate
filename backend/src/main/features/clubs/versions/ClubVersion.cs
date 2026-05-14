namespace backend.main.features.clubs.versions;

public class ClubVersion
{
    public int Id { get; set; }
    public int ClubId { get; set; }
    public int VersionNumber { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public string ChangedFieldsJson { get; set; } = "[]";
    public string? ClubImage { get; set; }
    public int ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public int? RollbackSourceVersionNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
