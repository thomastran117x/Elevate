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
                ImageUrls = OrderGallery(ev).Select(i => i.ImageUrl).ToList(),
                CoverImageUrl = ResolveCoverUrl(ev),
                Images = MapGallery(ev),
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

        /// <summary>
        /// The gallery in display order, cover first. Every client that shows a single picture
        /// reads <c>imageUrls[0]</c>, so leading with the cover is what makes an explicit cover
        /// selection take effect without each of them having to change.
        /// </summary>
        private static IEnumerable<images.EventImage> OrderGallery(Events ev) =>
            ev.Images
                .OrderByDescending(i => i.IsCover)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.Id);

        /// <remarks>
        /// Falls back to the first image by sort order. An event whose rows predate the cover
        /// column, or whose cover was just deleted, still resolves to something.
        /// </remarks>
        private static string? ResolveCoverUrl(Events ev) =>
            OrderGallery(ev).FirstOrDefault()?.ImageUrl;

        private static List<EventImageResponse> MapGallery(Events ev)
        {
            var ordered = OrderGallery(ev).ToList();
            var coverId = ordered.FirstOrDefault()?.Id;

            return ordered
                .Select(image => new EventImageResponse
                {
                    Id = image.Id,
                    Url = image.ImageUrl,
                    AltText = image.AltText,
                    IsDecorative = image.IsDecorative,
                    // Reports the resolved cover, not the raw column, so the flagged image always
                    // matches the one CoverImageUrl points at.
                    IsCover = image.Id == coverId,
                    SortOrder = image.SortOrder,
                    NeedsAltText = image.NeedsAltText,
                    CreatedAt = image.CreatedAt,
                    UpdatedAt = image.UpdatedAt
                })
                .ToList();
        }

        public static EventImageResponse MapImageToResponse(images.EventImage image) => new()
        {
            Id = image.Id,
            Url = image.ImageUrl,
            AltText = image.AltText,
            IsDecorative = image.IsDecorative,
            IsCover = image.IsCover,
            SortOrder = image.SortOrder,
            NeedsAltText = image.NeedsAltText,
            CreatedAt = image.CreatedAt,
            UpdatedAt = image.UpdatedAt
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
                ImageUrls = OrderGallery(ev).Select(i => i.ImageUrl).ToList(),
                CoverImageUrl = ResolveCoverUrl(ev),
                Images = MapGallery(ev),
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


