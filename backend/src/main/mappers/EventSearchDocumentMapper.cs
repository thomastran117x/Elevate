using backend.main.models.core;
using backend.main.models.documents;

using Elastic.Clients.Elasticsearch;

namespace backend.main.Mappers
{
    public static class EventSearchDocumentMapper
    {
        public static EventDocument ToDocument(Events ev) => new()
        {
            Id = ev.Id,
            ClubId = ev.ClubId,
            Name = ev.Name,
            Description = ev.Description,
            Location = ev.Location,
            IsPrivate = ev.isPrivate,
            StartTime = ev.StartTime,
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
