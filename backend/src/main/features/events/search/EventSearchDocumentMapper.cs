using backend.main.features.events;

using Elastic.Clients.Elasticsearch;

namespace backend.main.features.events.search
{
    public static class EventSearchDocumentMapper
    {
        public static EventDocument ToDocument(Events ev) => new()
        {
            Id = ev.Id,
            ClubId = ev.ClubId,
            Name = ev.Name ?? string.Empty,
            Description = ev.Description ?? string.Empty,
            Location = ev.Location ?? string.Empty,
            IsPrivate = ev.isPrivate,
            LifecycleState = ev.LifecycleState.ToString(),
            StartTime = ev.StartTime ?? ev.CreatedAt,
            EndTime = ev.EndTime,
            CreatedAt = ev.CreatedAt,
            UpdatedAt = ev.UpdatedAt,
            Category = ev.Category.ToString(),
            VenueName = ev.VenueName,
            City = ev.City,
            Tags = ev.Tags ?? new List<string>(),
            LocationGeo = (ev.Latitude.HasValue && ev.Longitude.HasValue)
                ? GeoLocation.LatitudeLongitude(new LatLonGeoLocation
                {
                    Lat = ev.Latitude.Value,
                    Lon = ev.Longitude.Value
                })
                : null,
            RegistrationCount = ev.RegistrationCount
        };
    }
}
