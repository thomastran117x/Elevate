using backend.main.dtos.responses.events;
using backend.main.models.core;
using backend.main.models.enums;

namespace backend.main.Mappers
{
    public static class EventMapper
    {
        public static EventResponse MapToResponse(Events ev) => new()
        {
            Id = ev.Id,
            Name = ev.Name,
            Description = ev.Description,
            Location = ev.Location,
            ImageUrl = ev.ImageUrl,
            IsPrivate = ev.isPrivate,
            MaxParticipants = ev.maxParticipants,
            RegisterCost = ev.registerCost,
            StartTime = ev.StartTime,
            EndTime = ev.EndTime,
            ClubId = ev.ClubId,
            CreatedAt = ev.CreatedAt,
            Status = ResolveStatus(ev)
        };

        public static EventStatus ResolveStatus(Events ev)
        {
            var now = DateTime.UtcNow;
            if (ev.StartTime > now)
                return EventStatus.Upcoming;
            if (ev.EndTime == null || ev.EndTime > now)
                return EventStatus.Ongoing;
            return EventStatus.Closed;
        }
    }
}
