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
                WaitlistEnabled = ev.WaitlistEnabled,
                WaitlistCount = ev.WaitlistCount,
                SeriesId = ev.SeriesId,
                OccurrenceIndex = ev.OccurrenceIndex,
                TimeZoneId = ev.TimeZoneId,
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

        /// <summary>
        /// Maps an event to the organizer-facing shape, including the lifecycle moves currently
        /// available to them and the consequences of each.
        /// </summary>
        /// <param name="ev">The event to map.</param>
        /// <param name="publishIssues">
        /// Outstanding publish blockers from <see cref="EventLifecyclePolicy.GetPublishIssues"/>.
        /// </param>
        /// <param name="revertAvailableUntil">
        /// Deadline for undoing the last lifecycle change, from
        /// <see cref="EventLifecyclePolicy.GetRevertAvailableUntil"/>. Null suppresses the undo
        /// affordance, which is what the series screens want: an occurrence's lifecycle is undone
        /// from the event itself, not from a list of siblings.
        /// </param>
        public static ManagedEventResponse MapToManagedResponse(
            Events ev,
            IReadOnlyList<string> publishIssues,
            DateTime? revertAvailableUntil = null) => new()
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
                WaitlistEnabled = ev.WaitlistEnabled,
                WaitlistCount = ev.WaitlistCount,
                SeriesId = ev.SeriesId,
                OccurrenceIndex = ev.OccurrenceIndex,
                SeriesOverridden = ev.SeriesOverridden,
                TimeZoneId = ev.TimeZoneId,
                PublishReady = publishIssues.Count == 0,
                PublishIssues = publishIssues.ToList(),
                LifecycleChangedAt = ev.LifecycleChangedAt,
                PreviousLifecycleState = ev.PreviousLifecycleState,
                RevertAvailableUntil = revertAvailableUntil,
                AvailableTransitions = EventLifecyclePolicy
                    .GetAvailableTransitions(ev, DateTime.UtcNow)
                    .Select(MapToTransitionResponse)
                    .ToList()
            };

        private static EventLifecycleTransitionResponse MapToTransitionResponse(
            EventLifecycleTransition transition) => new()
            {
                Key = transition.Key,
                Target = transition.Target,
                Label = transition.Label,
                Title = transition.Title,
                IsReversible = transition.IsReversible,
                ReversibleNote = transition.ReversibleNote,
                IsDestructive = transition.IsDestructive,
                Impacts = transition.Impacts.ToList(),
                BlockedReason = transition.BlockedReason
            };

        public static EventStatus ResolveStatus(Events ev)
        {
            return ResolveOptionalStatus(ev) ?? EventStatus.Upcoming;
        }

        public static EventStatus? ResolveOptionalStatus(Events ev) =>
            EventLifecyclePolicy.ResolveStatus(ev, DateTime.UtcNow);
    }
}


