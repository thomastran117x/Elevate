using backend.main.features.clubs;
using backend.main.features.events.contracts.responses;
using backend.main.shared.responses;

namespace backend.main.features.events
{
    public static class EventMapper
    {
        public static EventResponse MapToResponse(
            Events ev,
            double? distanceKm = null) => new()
            {
                Id = ev.Id,
                Name = ev.Name ?? string.Empty,
                Description = ev.Description ?? string.Empty,
                Location = ev.Location ?? string.Empty,
                ImageUrls = ev.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).ToList(),
                IsPrivate = ev.isPrivate,
                MaxParticipants = ev.maxParticipants,
                RegisterCost = ev.registerCost,
                StartTime = ev.StartTime ?? ev.CreatedAt,
                EndTime = ev.EndTime,
                ClubId = ev.ClubId,
                CurrentVersionNumber = ev.CurrentVersionNumber,
                CreatedAt = ev.CreatedAt,
                LifecycleState = ev.LifecycleState,
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

        public static ManagedEventResponse MapToManagedResponse(
            Events ev,
            IReadOnlyList<string> publishIssues) => new()
            {
                Id = ev.Id,
                Name = ev.Name,
                Description = ev.Description,
                Location = ev.Location,
                ImageUrls = ev.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).ToList(),
                IsPrivate = ev.isPrivate,
                MaxParticipants = ev.maxParticipants == 0 ? null : ev.maxParticipants,
                RegisterCost = ev.registerCost,
                StartTime = ev.StartTime,
                EndTime = ev.EndTime,
                ClubId = ev.ClubId,
                CurrentVersionNumber = ev.CurrentVersionNumber,
                CreatedAt = ev.CreatedAt,
                UpdatedAt = ev.UpdatedAt,
                Status = ResolveOptionalStatus(ev),
                LifecycleState = ev.LifecycleState,
                Category = ev.Category,
                VenueName = ev.VenueName,
                City = ev.City,
                Latitude = ev.Latitude,
                Longitude = ev.Longitude,
                Tags = ev.Tags ?? new List<string>(),
                RegistrationCount = ev.RegistrationCount,
                PublishReady = publishIssues.Count == 0,
                PublishIssues = publishIssues.ToList()
            };

        public static EventStatus ResolveStatus(Events ev)
        {
            return ResolveOptionalStatus(ev) ?? EventStatus.Upcoming;
        }

        public static EventStatus? ResolveOptionalStatus(Events ev) =>
            EventLifecyclePolicy.ResolveStatus(ev, DateTime.UtcNow);
    }
}


