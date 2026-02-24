namespace backend.main.Models;

public class Events
{
    public int Id
    {
        get; set;
    }
    public required string Name
    {
        get; set;
    }
    public required string Description
    {
        get; set;
    }
    public required string Location
    {
        get; set;
    }
    public required string ImageUrl
    {
        get; set;
    }
    public bool isPrivate { get; set; } = false;
    public int maxParticipants { get; set; } = 100;
    public int registerCost { get; set; } = 0;
    public required DateTime StartTime
    {
        get; set;
    }
    public DateTime? EndTime
    {
        get; set;
    }
    public int ClubId
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
