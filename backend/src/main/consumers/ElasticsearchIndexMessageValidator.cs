using backend.main.dtos.messages;
using backend.main.features.clubs.posts.search;
using backend.main.models.documents;

using Elastic.Clients.Elasticsearch;

namespace backend.main.consumers
{
    public sealed class ElasticsearchIndexMessageValidationException : Exception
    {
        public ElasticsearchIndexMessageValidationException(string message)
            : base(message) { }
    }

    public static class ElasticsearchIndexMessageValidator
    {
        public static bool IsDeleteOperation(string operation) =>
            string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase);

        public static EventDocument ToEventDocument(EventIndexEvent evt)
        {
            ValidateOperation(evt.Operation, "event", evt.EventId);

            if (IsDeleteOperation(evt.Operation))
                throw new ElasticsearchIndexMessageValidationException(
                    $"Delete event message {evt.EventId} cannot be mapped to an Elasticsearch document.");

            var clubId = RequirePositive(evt.ClubId, "clubId");
            var name = RequireText(evt.Name, "name");
            var location = RequireText(evt.Location, "location");
            var startTime = RequireValue(evt.StartTime, "startTime");
            var createdAt = RequireValue(evt.CreatedAt, "createdAt");
            var updatedAt = RequireValue(evt.UpdatedAt, "updatedAt");
            var category = evt.Category
                ?? throw new ElasticsearchIndexMessageValidationException("Event category is required.");
            var registrationCount = evt.RegistrationCount ?? 0;

            if (registrationCount < 0)
                throw new ElasticsearchIndexMessageValidationException(
                    "registrationCount cannot be negative.");

            if (evt.EndTime.HasValue && evt.EndTime.Value < startTime)
                throw new ElasticsearchIndexMessageValidationException(
                    "endTime cannot be earlier than startTime.");

            if (updatedAt < createdAt)
                throw new ElasticsearchIndexMessageValidationException(
                    "updatedAt cannot be earlier than createdAt.");

            var geo = BuildGeoLocation(evt.Latitude, evt.Longitude);

            return new EventDocument
            {
                Id = evt.EventId,
                ClubId = clubId,
                Name = name,
                Description = NormalizeOptionalText(evt.Description),
                Location = location,
                IsPrivate = evt.IsPrivate ?? false,
                StartTime = startTime,
                EndTime = evt.EndTime,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                Category = category.ToString(),
                VenueName = NormalizeOptionalText(evt.VenueName, allowNull: true),
                City = NormalizeOptionalText(evt.City, allowNull: true),
                Tags = NormalizeTags(evt.Tags),
                LocationGeo = geo,
                RegistrationCount = registrationCount
            };
        }

        public static ClubPostDocument ToClubPostDocument(ClubPostIndexEvent evt)
        {
            ValidateOperation(evt.Operation, "club post", evt.PostId);

            if (IsDeleteOperation(evt.Operation))
                throw new ElasticsearchIndexMessageValidationException(
                    $"Delete club post message {evt.PostId} cannot be mapped to an Elasticsearch document.");

            var clubId = RequirePositive(evt.ClubId, "clubId");
            var userId = RequirePositive(evt.UserId, "userId");
            var title = RequireText(evt.Title, "title");
            var postType = RequireText(evt.PostType, "postType");
            var createdAt = RequireValue(evt.CreatedAt, "createdAt");
            var updatedAt = RequireValue(evt.UpdatedAt, "updatedAt");
            var likesCount = evt.LikesCount ?? 0;

            if (likesCount < 0)
                throw new ElasticsearchIndexMessageValidationException(
                    "likesCount cannot be negative.");

            if (updatedAt < createdAt)
                throw new ElasticsearchIndexMessageValidationException(
                    "updatedAt cannot be earlier than createdAt.");

            return new ClubPostDocument
            {
                Id = evt.PostId,
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Content = NormalizeOptionalText(evt.Content),
                PostType = postType,
                LikesCount = likesCount,
                IsPinned = evt.IsPinned ?? false,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        public static void ValidateDelete(EventIndexEvent evt)
        {
            ValidateOperation(evt.Operation, "event", evt.EventId);
            if (!IsDeleteOperation(evt.Operation))
                throw new ElasticsearchIndexMessageValidationException(
                    $"Event message {evt.EventId} is not a delete operation.");
        }

        public static void ValidateDelete(ClubPostIndexEvent evt)
        {
            ValidateOperation(evt.Operation, "club post", evt.PostId);
            if (!IsDeleteOperation(evt.Operation))
                throw new ElasticsearchIndexMessageValidationException(
                    $"Club post message {evt.PostId} is not a delete operation.");
        }

        private static void ValidateOperation(string? operation, string entityName, int entityId)
        {
            if (entityId <= 0)
                throw new ElasticsearchIndexMessageValidationException(
                    $"{entityName} id must be a positive integer.");

            if (string.IsNullOrWhiteSpace(operation))
                throw new ElasticsearchIndexMessageValidationException(
                    $"{entityName} operation is required.");

            if (!string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(operation, "upsert", StringComparison.OrdinalIgnoreCase))
            {
                throw new ElasticsearchIndexMessageValidationException(
                    $"{entityName} operation '{operation}' is not supported.");
            }
        }

        private static int RequirePositive(int? value, string fieldName)
        {
            if (!value.HasValue || value.Value <= 0)
                throw new ElasticsearchIndexMessageValidationException(
                    $"{fieldName} must be a positive integer.");

            return value.Value;
        }

        private static DateTime RequireValue(DateTime? value, string fieldName)
        {
            if (!value.HasValue)
                throw new ElasticsearchIndexMessageValidationException(
                    $"{fieldName} is required.");

            return value.Value;
        }

        private static string RequireText(string? value, string fieldName)
        {
            var normalized = NormalizeOptionalText(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ElasticsearchIndexMessageValidationException(
                    $"{fieldName} is required.");

            return normalized;
        }

        private static string? NormalizeOptionalText(string? value, bool allowNull = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return allowNull ? null : string.Empty;

            return value.Trim();
        }

        private static List<string> NormalizeTags(List<string>? tags) =>
            tags?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .Take(10)
                .ToList()
            ?? new List<string>();

        private static GeoLocation? BuildGeoLocation(double? latitude, double? longitude)
        {
            if (latitude.HasValue != longitude.HasValue)
            {
                throw new ElasticsearchIndexMessageValidationException(
                    "latitude and longitude must both be provided together.");
            }

            if (!latitude.HasValue || !longitude.HasValue)
                return null;

            if (latitude.Value < -90 || latitude.Value > 90)
                throw new ElasticsearchIndexMessageValidationException(
                    "latitude must be between -90 and 90.");

            if (longitude.Value < -180 || longitude.Value > 180)
                throw new ElasticsearchIndexMessageValidationException(
                    "longitude must be between -180 and 180.");

            return GeoLocation.LatitudeLongitude(new LatLonGeoLocation
            {
                Lat = latitude.Value,
                Lon = longitude.Value
            });
        }
    }
}
