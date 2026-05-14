using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;

namespace backend.main.seeders;

public sealed record SeedUserDefinition(
    string Email,
    string Username,
    string Name,
    string Role);

public sealed record SeedVenueDefinition(
    string Name,
    string Location,
    double Latitude,
    double Longitude);

public sealed record SeedEventSeriesDefinition(
    string NamePrefix,
    string DescriptionTemplate,
    EventCategory Category,
    IReadOnlyList<SeedVenueDefinition> Venues,
    IReadOnlyList<string> Tags,
    int OccurrenceCount,
    int StartDayOffset,
    int CadenceDays,
    int StartHourUtc,
    int DurationHours,
    int BaseMaxParticipants,
    int BaseRegisterCost,
    bool IsPrivate = false,
    int CapacityStep = 0,
    int CostStep = 0);

public sealed record SeedEventDefinition(
    string Name,
    string Description,
    string Location,
    bool IsPrivate,
    int MaxParticipants,
    int RegisterCost,
    DateTime StartTimeUtc,
    DateTime? EndTimeUtc,
    EventCategory Category,
    string VenueName,
    string City,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Tags,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public enum SeedClubAuthorRole
{
    Owner,
    Manager,
    Volunteer
}

public sealed record SeedClubPostDefinition(
    string TitleTemplate,
    string ContentTemplate,
    PostType PostType,
    bool IsPinned,
    SeedClubAuthorRole AuthorRole,
    int DayOffset,
    int LikesCount = 0,
    int ViewCount = 0);

public sealed record SeedClubDefinition(
    string Slug,
    string Name,
    string Description,
    ClubType ClubType,
    string Theme,
    string Tone,
    string ClubImage,
    string Email,
    string Phone,
    string WebsiteUrl,
    string Location,
    string City,
    int MaxMemberCount,
    string OwnerEmail,
    string ManagerEmail,
    string VolunteerEmail,
    IReadOnlyList<string> ThemeTags,
    IReadOnlyList<SeedClubPostDefinition> Posts,
    IReadOnlyList<SeedEventSeriesDefinition> PublicSeries,
    IReadOnlyList<SeedEventSeriesDefinition> PrivateSeries);

public interface IClubSeedDefinitionSource
{
    SeedClubDefinition Definition { get; }
}
