namespace backend.main.features.auth.history;

/// <summary>
/// A security history entry waiting for Debezium to relay it to the security-history topic.
/// </summary>
/// <remarks>
/// Same shape and column names as the search outboxes, so the same EventRouter configuration
/// applies. Rows are never updated; the security-history worker deletes them once they are old
/// enough to have been relayed (Debezium reads the WAL, and the router ignores deletes).
/// </remarks>
public class SecurityHistoryOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
