using backend.main.features.events.images;

namespace backend.main.features.events;

public class Events
{
    public int Id
    {
        get; set;
    }
    public string? Name
    {
        get; set;
    }
    public string? Description
    {
        get; set;
    }
    public string? Location
    {
        get; set;
    }
    public bool isPrivate { get; set; } = false;
    public int maxParticipants { get; set; } = 0;
    public int registerCost { get; set; } = 0;
    public DateTime? StartTime
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
    public EventLifecycleState LifecycleState { get; set; } = EventLifecycleState.Draft;
    public int CurrentVersionNumber { get; set; } = 0;
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

    /// <summary>
    /// Opt-in: when the event is full, users may join a waitlist and are auto-promoted
    /// into a registration as seats free up. Requires a capacity limit and a free event.
    /// </summary>
    public bool WaitlistEnabled { get; set; } = false;

    /// <summary>
    /// Denormalized count of entries with Status == Waiting. Mirrors RegistrationCount so
    /// the value reaches the cached event entity (and therefore EventResponse) for free.
    /// </summary>
    public int WaitlistCount { get; set; } = 0;

    // Navigation
    public ICollection<EventImage> Images { get; set; } = new List<EventImage>();
}

