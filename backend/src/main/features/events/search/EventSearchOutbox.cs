namespace backend.main.features.events.search;

public class EventSearchOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


