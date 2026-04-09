namespace backend.main.models.core;

public class EventImage
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public required string ImageUrl { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Events Event { get; set; } = null!;
}
