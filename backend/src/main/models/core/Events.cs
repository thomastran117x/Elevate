using backend.main.models.enums;

namespace backend.main.models.core;

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

    public EventCategory Category
    {
        get; set;
    } = EventCategory.Other;
    public string? VenueName
    {
        get; set;
    }
    public string? City
    {
        get; set;
    }
    public double? Latitude
    {
        get; set;
    }
    public double? Longitude
    {
        get; set;
    }
    public List<string> Tags
    {
        get; set;
    } = new List<string>();
    public int RegistrationCount
    {
        get; set;
    } = 0;

    // Navigation
    public ICollection<EventImage> Images { get; set; } = new List<EventImage>();
}
