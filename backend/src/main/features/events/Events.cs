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

    /// <summary>
    /// The recurrence series this event is an occurrence of, or null for a standalone event.
    /// Held as a bare scalar FK with no navigation property: adding one would force an
    /// Include into all five query paths in EventsRepository and enlarge the Redis payload,
    /// for data the read path never needs joined.
    /// </summary>
    public int? SeriesId
    {
        get; set;
    }

    /// <summary>Zero-based position within <see cref="SeriesId"/>'s generated sequence.</summary>
    public int? OccurrenceIndex
    {
        get; set;
    }

    /// <summary>
    /// Set once this occurrence has been edited on its own. "Update all future occurrences"
    /// skips overridden rows by default so a deliberate one-off tweak is not silently undone.
    /// </summary>
    public bool SeriesOverridden { get; set; } = false;

    /// <summary>
    /// IANA time zone the event's wall-clock time is anchored to, denormalized from the series
    /// at generation time. Null for events created before recurrence existed, which retain the
    /// codebase's older "UTC by assumption" behaviour.
    /// </summary>
    public string? TimeZoneId
    {
        get; set;
    }

    // Navigation
    public ICollection<EventImage> Images { get; set; } = new List<EventImage>();
}

