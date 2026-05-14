namespace backend.main.features.events.versions;

public class EventVersion
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int VersionNumber { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public string ChangedFieldsJson { get; set; } = "[]";
    public int ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public int? RollbackSourceVersionNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
