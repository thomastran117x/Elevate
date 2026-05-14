using backend.main.features.events.contracts.responses;
using backend.main.shared.responses;
using backend.main.features.clubs;

namespace backend.main.features.events
{
    public static class EventMapper
    {
        public static EventResponse MapToResponse(
            Events ev,
            double? distanceKm = null) => new()
        {
            Id = ev.Id,
            Name = ev.Name,
            Description = ev.Description,
            Location = ev.Location,
            ImageUrls = ev.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).ToList(),
            IsPrivate = ev.isPrivate,
            MaxParticipants = ev.maxParticipants,
            RegisterCost = ev.registerCost,
            StartTime = ev.StartTime,
            EndTime = ev.EndTime,
            ClubId = ev.ClubId,
            CurrentVersionNumber = ev.CurrentVersionNumber,
            CreatedAt = ev.CreatedAt,
            Status = ResolveStatus(ev),
            Category = ev.Category,
            VenueName = ev.VenueName,
            City = ev.City,
            Latitude = ev.Latitude,
            Longitude = ev.Longitude,
            Tags = ev.Tags ?? new List<string>(),
            RegistrationCount = ev.RegistrationCount,
            DistanceKm = distanceKm
        };

        public static EventHostClubResponse MapClubToResponse(Club club) => new()
        {
            Id = club.Id,
            Name = club.Name,
            Description = club.Description,
            ClubType = club.Clubtype.ToString(),
            ClubImage = club.ClubImage,
            MemberCount = club.MemberCount,
            EventCount = club.EventCount,
            AvailableEventCount = club.AvaliableEventCount,
            IsPrivate = club.isPrivate,
            Email = club.Email,
            Phone = club.Phone,
            Rating = club.Rating,
            WebsiteUrl = club.WebsiteUrl,
            Location = club.Location
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



