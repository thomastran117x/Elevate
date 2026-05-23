using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.events;
using backend.main.features.events.invitations;
using backend.main.features.payment;

namespace backend.main.seeders;

public sealed record SeedUserDefinition(
    string Email,
    string Username,
    string Name,
    string Role);

public sealed record SeedClubFollowDefinition(
    string ClubSlug,
    string UserEmail,
    int DayOffset);

public sealed record SeedClubReviewDefinition(
    string ClubSlug,
    string UserEmail,
    string Title,
    int Rating,
    string? Comment,
    int DayOffset);

public sealed record SeedPostCommentDefinition(
    string ClubSlug,
    string PostTitle,
    string UserEmail,
    string Content,
    int DayOffset);

public sealed record SeedEventImageSetDefinition(
    string ClubSlug,
    string EventName,
    IReadOnlyList<string> ImageUrls);

public sealed record SeedEventRegistrationDefinition(
    string ClubSlug,
    string EventName,
    string UserEmail,
    int DayOffset);

public sealed record SeedEventInvitationLinkDefinition(
    string ClubSlug,
    string EventName,
    string CreatedByEmail,
    int MaxRedemptions,
    int RedemptionCount,
    int DayOffset,
    int ExpiresInDays,
    bool IsRevoked);

public sealed record SeedEventInvitationDefinition(
    string ClubSlug,
    string EventName,
    EventInvitationSource SourceType,
    EventInvitationLifecycleStatus LifecycleStatus,
    EventInvitationDeliveryStatus DeliveryStatus,
    string CreatedByEmail,
    string? RecipientUserEmail,
    string? RecipientEmail,
    string? LinkRecipientUserEmail,
    int DayOffset,
    int ExpiresInDays,
    bool IsLinkBased = false,
    bool IsRevoked = false,
    string? DeliveryError = null);

public sealed record SeedPaymentDefinition(
    string ClubSlug,
    string EventName,
    string UserEmail,
    long Amount,
    string Currency,
    PaymentStatus Status,
    int DayOffset);

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
    SeedClubDefinition Definition
    {
        get;
    }
}
