using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.search;
using backend.main.features.clubs.versions;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.seeders;

public sealed class SeedFeatureActivitySeeder : ISeeder
{
    private readonly AppDatabaseContext _dbContext;
    private readonly IEventSearchOutboxWriter _eventOutboxWriter;
    private readonly IClubSearchOutboxWriter _clubOutboxWriter;
    private readonly IEnumerable<IClubSeedDefinitionSource> _clubSeedSources;
    private readonly ILogger<SeedFeatureActivitySeeder> _logger;

    public SeedFeatureActivitySeeder(
        AppDatabaseContext dbContext,
        IEventSearchOutboxWriter eventOutboxWriter,
        IClubSearchOutboxWriter clubOutboxWriter,
        IEnumerable<IClubSeedDefinitionSource> clubSeedSources,
        ILogger<SeedFeatureActivitySeeder> logger)
    {
        _dbContext = dbContext;
        _eventOutboxWriter = eventOutboxWriter;
        _clubOutboxWriter = clubOutboxWriter;
        _clubSeedSources = clubSeedSources;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var clubDefinitions = _clubSeedSources
            .Select(source => source.Definition)
            .OrderBy(definition => definition.Slug, StringComparer.Ordinal)
            .ToList();

        var desiredUserEmails = UserSeedCatalog.All
            .Select(user => user.Email)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var users = await _dbContext.Users
            .Where(user => desiredUserEmails.Contains(user.Email))
            .ToListAsync(cancellationToken);
        var usersByEmail = users.ToDictionary(user => user.Email, StringComparer.OrdinalIgnoreCase);

        var missingUsers = desiredUserEmails
            .Where(email => !usersByEmail.ContainsKey(email))
            .ToList();

        if (missingUsers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Feature activity seeding requires all seed users to exist first. Missing users: {string.Join(", ", missingUsers)}");
        }

        var desiredClubNames = clubDefinitions
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var clubs = await _dbContext.Clubs
            .Where(club => desiredClubNames.Contains(club.Name))
            .ToListAsync(cancellationToken);
        var clubsByName = clubs.ToDictionary(club => club.Name, StringComparer.OrdinalIgnoreCase);

        var missingClubs = clubDefinitions
            .Where(definition => !clubsByName.ContainsKey(definition.Name))
            .Select(definition => definition.Name)
            .ToList();

        if (missingClubs.Count > 0)
        {
            throw new InvalidOperationException(
                $"Feature activity seeding requires themed clubs to exist first. Missing clubs: {string.Join(", ", missingClubs)}");
        }

        var clubIds = clubs.Select(club => club.Id).ToList();
        var events = await _dbContext.Events
            .Where(ev => clubIds.Contains(ev.ClubId))
            .OrderBy(ev => ev.StartTime)
            .ThenBy(ev => ev.Name)
            .ToListAsync(cancellationToken);
        var posts = await _dbContext.ClubPosts
            .Where(post => clubIds.Contains(post.ClubId))
            .OrderBy(post => post.CreatedAt)
            .ThenBy(post => post.Title)
            .ToListAsync(cancellationToken);

        var resolvedClubs = clubDefinitions
            .Select(definition => ResolveClubContext(definition, clubsByName[definition.Name], events, posts))
            .ToList();

        var seedUserIds = usersByEmail.Values
            .Select(user => user.Id)
            .ToHashSet();
        var seedClubIds = resolvedClubs
            .Select(club => club.Club.Id)
            .ToHashSet();
        var seedEventIds = resolvedClubs
            .SelectMany(club => club.PublicEvents.Concat(club.PrivateEvents))
            .Select(ev => ev.Id)
            .ToHashSet();
        var seedPostIds = resolvedClubs
            .SelectMany(club => club.Posts)
            .Select(post => post.Id)
            .ToHashSet();

        await CleanupManagedActivityAsync(seedUserIds, seedClubIds, seedEventIds, seedPostIds, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var follows = BuildFollowDefinitions(resolvedClubs);
        var reviews = BuildReviewDefinitions(resolvedClubs);
        var comments = BuildCommentDefinitions(resolvedClubs);
        var imageSets = BuildImageDefinitions(resolvedClubs);
        var registrations = BuildRegistrationDefinitions(resolvedClubs);
        var registrationsByEventName = registrations
            .GroupBy(definition => $"{definition.ClubSlug}|{definition.EventName}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var payments = BuildPaymentDefinitions(resolvedClubs, registrationsByEventName);
        var invitationLinks = BuildInvitationLinkDefinitions(resolvedClubs);
        var invitations = BuildInvitationDefinitions(resolvedClubs);

        var followEntities = follows
            .Select((definition, index) => new FollowClub
            {
                ClubId = resolvedClubs.Single(club => club.Definition.Slug == definition.ClubSlug).Club.Id,
                UserId = usersByEmail[definition.UserEmail].Id,
                CreatedAt = BuildActivityTimestamp(index + definition.DayOffset + 1),
                UpdatedAt = BuildActivityTimestamp(index + definition.DayOffset + 2)
            })
            .ToList();

        var reviewEntities = reviews
            .Select((definition, index) => new ClubReview
            {
                ClubId = resolvedClubs.Single(club => club.Definition.Slug == definition.ClubSlug).Club.Id,
                UserId = usersByEmail[definition.UserEmail].Id,
                Title = definition.Title,
                Rating = definition.Rating,
                Comment = definition.Comment,
                CreatedAt = BuildActivityTimestamp(index + definition.DayOffset + 3),
                UpdatedAt = BuildActivityTimestamp(index + definition.DayOffset + 4)
            })
            .ToList();

        var commentEntities = comments
            .Select((definition, index) =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var post = club.Posts.Single(entry => string.Equals(entry.Title, definition.PostTitle, StringComparison.OrdinalIgnoreCase));

                return new PostComment
                {
                    PostId = post.Id,
                    UserId = usersByEmail[definition.UserEmail].Id,
                    Content = definition.Content,
                    CreatedAt = post.CreatedAt.AddDays(definition.DayOffset + 1).AddMinutes(index + 1),
                    UpdatedAt = post.CreatedAt.AddDays(definition.DayOffset + 1).AddMinutes(index + 3)
                };
            })
            .ToList();

        var imageEntities = imageSets
            .SelectMany(definition =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var ev = club.PublicEvents
                    .Concat(club.PrivateEvents)
                    .Single(entry => string.Equals(entry.Name, definition.EventName, StringComparison.OrdinalIgnoreCase));

                return definition.ImageUrls
                    .Select((imageUrl, index) => new EventImage
                    {
                        EventId = ev.Id,
                        ImageUrl = imageUrl,
                        SortOrder = index,
                        CreatedAt = ev.CreatedAt.AddMinutes(index + 1)
                    });
            })
            .ToList();

        var registrationEntities = registrations
            .Select((definition, index) =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var ev = club.PublicEvents.Single(entry => string.Equals(entry.Name, definition.EventName, StringComparison.OrdinalIgnoreCase));

                return new EventRegistration
                {
                    EventId = ev.Id,
                    UserId = usersByEmail[definition.UserEmail].Id,
                    CreatedAt = ev.StartTime!.Value.AddDays(-Math.Abs(definition.DayOffset + 2)).AddMinutes(index)
                };
            })
            .ToList();

        var paymentEntities = payments
            .Select((definition, index) =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var ev = club.PublicEvents.Single(entry => string.Equals(entry.Name, definition.EventName, StringComparison.OrdinalIgnoreCase));
                var seedKey = $"{definition.ClubSlug}|{definition.EventName}|{definition.UserEmail}|{definition.Status}";
                var token = ComputeStableHash(seedKey);
                var createdAt = ev.StartTime!.Value.AddDays(-Math.Abs(definition.DayOffset + 3)).AddMinutes(index + 2);

                return new Payment
                {
                    UserId = usersByEmail[definition.UserEmail].Id,
                    EventId = ev.Id,
                    Amount = definition.Amount,
                    Currency = definition.Currency,
                    Status = definition.Status,
                    IdempotencyKey = $"seed:payment:{token[..24]}",
                    ExternalSessionId = $"seed_session_{token[..24]}",
                    ExternalPaymentIntentId = definition.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded
                        ? $"seed_pi_{token[..24]}"
                        : null,
                    CheckoutUrl = $"https://checkout.seed.eventxperience.test/pay/{token[..24]}",
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt.AddMinutes(2)
                };
            })
            .ToList();

        var linkEntitiesByKey = invitationLinks.ToDictionary(
            definition => $"{definition.ClubSlug}|{definition.EventName}",
            definition =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var ev = club.PrivateEvents.Single(entry => string.Equals(entry.Name, definition.EventName, StringComparison.OrdinalIgnoreCase));
                var createdAt = ev.StartTime!.Value.AddDays(-Math.Abs(definition.DayOffset + 4));
                var link = new EventInvitationLink
                {
                    EventId = ev.Id,
                    TokenHash = ComputeStableHash($"seed:link:{definition.ClubSlug}:{definition.EventName}"),
                    ExpiresAt = createdAt.AddDays(definition.ExpiresInDays),
                    MaxRedemptions = definition.MaxRedemptions,
                    RedemptionCount = definition.RedemptionCount,
                    CreatedByUserId = usersByEmail[definition.CreatedByEmail].Id,
                    RevokedByUserId = definition.IsRevoked ? usersByEmail[definition.CreatedByEmail].Id : null,
                    RevokedAtUtc = definition.IsRevoked ? createdAt.AddDays(1) : null,
                    CreatedAt = createdAt,
                    UpdatedAt = definition.IsRevoked ? createdAt.AddDays(1) : createdAt.AddMinutes(10)
                };

                _dbContext.EventInvitationLinks.Add(link);
                return link;
            },
            StringComparer.OrdinalIgnoreCase);

        var invitationEntities = invitations
            .Select((definition, index) =>
            {
                var club = resolvedClubs.Single(entry => entry.Definition.Slug == definition.ClubSlug);
                var ev = club.PrivateEvents.Single(entry => string.Equals(entry.Name, definition.EventName, StringComparison.OrdinalIgnoreCase));
                var recipientEmail = definition.RecipientEmail ?? definition.RecipientUserEmail;
                var createdAt = ev.StartTime!.Value.AddDays(-Math.Abs(definition.DayOffset + 5)).AddMinutes(index + 1);
                var acceptedAt = definition.LifecycleStatus == EventInvitationLifecycleStatus.Accepted ? createdAt.AddHours(12) : (DateTime?)null;
                var declinedAt = definition.LifecycleStatus == EventInvitationLifecycleStatus.Declined ? createdAt.AddHours(10) : (DateTime?)null;
                var revokedAt = definition.IsRevoked ? createdAt.AddHours(8) : (DateTime?)null;

                return new EventInvitation
                {
                    EventId = ev.Id,
                    RecipientUserId = definition.RecipientUserEmail != null ? usersByEmail[definition.RecipientUserEmail].Id : null,
                    RecipientEmail = recipientEmail,
                    RecipientEmailNormalized = recipientEmail?.Trim().ToLowerInvariant(),
                    SourceType = definition.SourceType,
                    LifecycleStatus = definition.LifecycleStatus,
                    DeliveryStatus = definition.DeliveryStatus,
                    ClaimTokenHash = definition.IsLinkBased
                        ? null
                        : ComputeStableHash($"seed:invite:{definition.ClubSlug}:{definition.EventName}:{definition.SourceType}:{recipientEmail}:{definition.LifecycleStatus}"),
                    ExpiresAt = createdAt.AddDays(definition.ExpiresInDays),
                    EventInvitationLink = definition.IsLinkBased
                        ? linkEntitiesByKey[$"{definition.ClubSlug}|{definition.EventName}"]
                        : null,
                    AcceptedAtUtc = acceptedAt,
                    DeclinedAtUtc = declinedAt,
                    RevokedAtUtc = revokedAt,
                    CreatedByUserId = usersByEmail[definition.CreatedByEmail].Id,
                    AcceptedByUserId = definition.LifecycleStatus == EventInvitationLifecycleStatus.Accepted && definition.RecipientUserEmail != null
                        ? usersByEmail[definition.RecipientUserEmail].Id
                        : null,
                    DeclinedByUserId = definition.LifecycleStatus == EventInvitationLifecycleStatus.Declined && definition.RecipientUserEmail != null
                        ? usersByEmail[definition.RecipientUserEmail].Id
                        : null,
                    RevokedByUserId = definition.IsRevoked
                        ? usersByEmail[definition.CreatedByEmail].Id
                        : null,
                    DeliveryError = definition.DeliveryError,
                    CreatedAt = createdAt,
                    UpdatedAt = revokedAt ?? acceptedAt ?? declinedAt ?? createdAt.AddMinutes(5)
                };
            })
            .ToList();

        var clubVersionEntities = new List<ClubVersion>();
        foreach (var resolvedClub in resolvedClubs)
        {
            var currentSnapshot = BuildClubSnapshot(resolvedClub.Club);
            var previousSnapshot = BuildPreviousClubSnapshot(resolvedClub.Definition, currentSnapshot);

            resolvedClub.Club.CurrentVersionNumber = 2;

            clubVersionEntities.Add(new ClubVersion
            {
                ClubId = resolvedClub.Club.Id,
                VersionNumber = 1,
                ActionType = ClubVersionActions.Create,
                SnapshotJson = JsonSerializer.Serialize(previousSnapshot),
                ChangedFieldsJson = JsonSerializer.Serialize(BuildClubChangedFields(null, previousSnapshot)),
                ClubImage = previousSnapshot.ClubImage,
                ActorUserId = usersByEmail[resolvedClub.Definition.OwnerEmail].Id,
                ActorRole = AuthRoleValue(usersByEmail[resolvedClub.Definition.OwnerEmail]),
                CreatedAt = resolvedClub.Club.CreatedAt
            });

            clubVersionEntities.Add(new ClubVersion
            {
                ClubId = resolvedClub.Club.Id,
                VersionNumber = 2,
                ActionType = ClubVersionActions.Update,
                SnapshotJson = JsonSerializer.Serialize(currentSnapshot),
                ChangedFieldsJson = JsonSerializer.Serialize(BuildClubChangedFields(previousSnapshot, currentSnapshot)),
                ClubImage = currentSnapshot.ClubImage,
                ActorUserId = usersByEmail[resolvedClub.Definition.ManagerEmail].Id,
                ActorRole = AuthRoleValue(usersByEmail[resolvedClub.Definition.ManagerEmail]),
                CreatedAt = resolvedClub.Club.CreatedAt.AddDays(2)
            });
        }

        var eventVersionEntities = new List<EventVersion>();
        foreach (var resolvedClub in resolvedClubs)
        {
            foreach (var ev in resolvedClub.PublicEvents.Concat(resolvedClub.PrivateEvents))
            {
                var currentSnapshot = BuildEventSnapshot(ev);
                var hasAdditionalHistory = resolvedClub.PublicEvents.Take(1).Concat(resolvedClub.PrivateEvents.Take(1)).Any(seedEvent => seedEvent.Id == ev.Id);

                if (!hasAdditionalHistory)
                {
                    ev.CurrentVersionNumber = 1;
                    eventVersionEntities.Add(new EventVersion
                    {
                        EventId = ev.Id,
                        VersionNumber = 1,
                        ActionType = EventVersionActions.Create,
                        SnapshotJson = JsonSerializer.Serialize(currentSnapshot),
                        ChangedFieldsJson = JsonSerializer.Serialize(BuildEventChangedFields(null, currentSnapshot)),
                        ActorUserId = usersByEmail[resolvedClub.Definition.OwnerEmail].Id,
                        ActorRole = AuthRoleValue(usersByEmail[resolvedClub.Definition.OwnerEmail]),
                        CreatedAt = ev.CreatedAt
                    });

                    continue;
                }

                var previousSnapshot = BuildPreviousEventSnapshot(currentSnapshot);

                ev.CurrentVersionNumber = 2;

                eventVersionEntities.Add(new EventVersion
                {
                    EventId = ev.Id,
                    VersionNumber = 1,
                    ActionType = EventVersionActions.Create,
                    SnapshotJson = JsonSerializer.Serialize(previousSnapshot),
                    ChangedFieldsJson = JsonSerializer.Serialize(BuildEventChangedFields(null, previousSnapshot)),
                    ActorUserId = usersByEmail[resolvedClub.Definition.OwnerEmail].Id,
                    ActorRole = AuthRoleValue(usersByEmail[resolvedClub.Definition.OwnerEmail]),
                    CreatedAt = ev.CreatedAt
                });

                eventVersionEntities.Add(new EventVersion
                {
                    EventId = ev.Id,
                    VersionNumber = 2,
                    ActionType = EventVersionActions.Update,
                    SnapshotJson = JsonSerializer.Serialize(currentSnapshot),
                    ChangedFieldsJson = JsonSerializer.Serialize(BuildEventChangedFields(previousSnapshot, currentSnapshot)),
                    ActorUserId = usersByEmail[resolvedClub.Definition.ManagerEmail].Id,
                    ActorRole = AuthRoleValue(usersByEmail[resolvedClub.Definition.ManagerEmail]),
                    CreatedAt = ev.CreatedAt.AddDays(1)
                });
            }
        }

        _dbContext.FollowClubs.AddRange(followEntities);
        _dbContext.ClubReviews.AddRange(reviewEntities);
        _dbContext.PostComments.AddRange(commentEntities);
        _dbContext.EventImages.AddRange(imageEntities);
        _dbContext.EventRegistrations.AddRange(registrationEntities);
        _dbContext.Payments.AddRange(paymentEntities);
        _dbContext.EventInvitations.AddRange(invitationEntities);
        _dbContext.ClubVersions.AddRange(clubVersionEntities);
        _dbContext.EventVersions.AddRange(eventVersionEntities);

        ReconcileClubCounters(resolvedClubs, followEntities, reviewEntities);
        ReconcileEventCounters(resolvedClubs, registrationEntities);

        foreach (var club in resolvedClubs.Select(entry => entry.Club))
            _clubOutboxWriter.StageUpsert(club);

        foreach (var ev in resolvedClubs.SelectMany(entry => entry.PublicEvents.Concat(entry.PrivateEvents)))
            _eventOutboxWriter.StageUpsert(ev);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Seeders] Rebuilt feature activity. Participants: {ParticipantCount}, follows: {FollowCount}, reviews: {ReviewCount}, comments: {CommentCount}, images: {ImageCount}, registrations: {RegistrationCount}, payments: {PaymentCount}, invitation links: {LinkCount}, invitations: {InvitationCount}, club versions: {ClubVersionCount}, event versions: {EventVersionCount}.",
            UserSeedCatalog.Participants.Count,
            followEntities.Count,
            reviewEntities.Count,
            commentEntities.Count,
            imageEntities.Count,
            registrationEntities.Count,
            paymentEntities.Count,
            linkEntitiesByKey.Count,
            invitationEntities.Count,
            clubVersionEntities.Count,
            eventVersionEntities.Count);
    }

    private async Task CleanupManagedActivityAsync(
        IReadOnlySet<int> seedUserIds,
        IReadOnlySet<int> seedClubIds,
        IReadOnlySet<int> seedEventIds,
        IReadOnlySet<int> seedPostIds,
        CancellationToken cancellationToken)
    {
        var managedInvitations = await _dbContext.EventInvitations
            .Where(invitation =>
                seedEventIds.Contains(invitation.EventId) &&
                (
                    (invitation.CreatedByUserId.HasValue && seedUserIds.Contains(invitation.CreatedByUserId.Value)) ||
                    (invitation.RecipientUserId.HasValue && seedUserIds.Contains(invitation.RecipientUserId.Value)) ||
                    (invitation.RecipientEmailNormalized != null && invitation.RecipientEmailNormalized.EndsWith(SeedCatalogConstants.SeedEmailDomain))
                ))
            .ToListAsync(cancellationToken);
        var managedLinks = await _dbContext.EventInvitationLinks
            .Where(link => seedEventIds.Contains(link.EventId) && seedUserIds.Contains(link.CreatedByUserId))
            .ToListAsync(cancellationToken);
        var managedPayments = await _dbContext.Payments
            .Where(payment => seedEventIds.Contains(payment.EventId) && seedUserIds.Contains(payment.UserId))
            .ToListAsync(cancellationToken);
        var managedRegistrations = await _dbContext.EventRegistrations
            .Where(registration => seedEventIds.Contains(registration.EventId) && seedUserIds.Contains(registration.UserId))
            .ToListAsync(cancellationToken);
        var managedComments = await _dbContext.PostComments
            .Where(comment => seedPostIds.Contains(comment.PostId) && seedUserIds.Contains(comment.UserId))
            .ToListAsync(cancellationToken);
        var managedReviews = await _dbContext.ClubReviews
            .Where(review => seedClubIds.Contains(review.ClubId) && seedUserIds.Contains(review.UserId))
            .ToListAsync(cancellationToken);
        var managedFollows = await _dbContext.FollowClubs
            .Where(follow => seedClubIds.Contains(follow.ClubId) && seedUserIds.Contains(follow.UserId))
            .ToListAsync(cancellationToken);
        var managedImages = await _dbContext.EventImages
            .Where(image => seedEventIds.Contains(image.EventId))
            .ToListAsync(cancellationToken);
        var managedClubVersions = await _dbContext.ClubVersions
            .Where(version => seedClubIds.Contains(version.ClubId))
            .ToListAsync(cancellationToken);
        var managedEventVersions = await _dbContext.EventVersions
            .Where(version => seedEventIds.Contains(version.EventId))
            .ToListAsync(cancellationToken);

        _dbContext.EventInvitations.RemoveRange(managedInvitations);
        _dbContext.EventInvitationLinks.RemoveRange(managedLinks);
        _dbContext.Payments.RemoveRange(managedPayments);
        _dbContext.EventRegistrations.RemoveRange(managedRegistrations);
        _dbContext.PostComments.RemoveRange(managedComments);
        _dbContext.ClubReviews.RemoveRange(managedReviews);
        _dbContext.FollowClubs.RemoveRange(managedFollows);
        _dbContext.EventImages.RemoveRange(managedImages);
        _dbContext.ClubVersions.RemoveRange(managedClubVersions);
        _dbContext.EventVersions.RemoveRange(managedEventVersions);
    }

    private static ResolvedSeedClub ResolveClubContext(
        SeedClubDefinition definition,
        Club club,
        IReadOnlyList<Events> events,
        IReadOnlyList<ClubPost> posts)
    {
        var clubEvents = events
            .Where(ev => ev.ClubId == club.Id)
            .OrderBy(ev => ev.StartTime)
            .ThenBy(ev => ev.Name)
            .ToList();
        var clubPosts = posts
            .Where(post => post.ClubId == club.Id)
            .OrderBy(post => post.CreatedAt)
            .ThenBy(post => post.Title)
            .ToList();

        var publicEvents = clubEvents.Where(ev => !ev.isPrivate).ToList();
        var privateEvents = clubEvents.Where(ev => ev.isPrivate).ToList();

        if (publicEvents.Count < 3)
            throw new InvalidOperationException($"Club '{definition.Name}' needs at least three public events for feature activity seeding.");

        if (privateEvents.Count < 1)
            throw new InvalidOperationException($"Club '{definition.Name}' needs at least one private event for feature activity seeding.");

        if (clubPosts.Count < 2)
            throw new InvalidOperationException($"Club '{definition.Name}' needs at least two posts for feature activity seeding.");

        return new ResolvedSeedClub(definition, club, publicEvents, privateEvents, clubPosts);
    }

    private static IReadOnlyList<SeedClubFollowDefinition> BuildFollowDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        var participantEmails = UserSeedCatalog.Participants.Select(user => user.Email).ToList();
        var follows = new List<SeedClubFollowDefinition>();

        for (var index = 0; index < clubs.Count; index++)
        {
            var selectedParticipants = CircularSlice(participantEmails, index, 4);
            follows.AddRange(selectedParticipants.Select((email, offset) =>
                new SeedClubFollowDefinition(clubs[index].Definition.Slug, email, index + offset + 1)));
        }

        return follows;
    }

    private static IReadOnlyList<SeedClubReviewDefinition> BuildReviewDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        var reviews = new List<SeedClubReviewDefinition>();
        var ratingPattern = new[] { 5, 4, 5 };

        for (var index = 0; index < clubs.Count; index++)
        {
            var selectedParticipants = CircularSlice(UserSeedCatalog.Participants.Select(user => user.Email).ToList(), index, 3);

            for (var ratingIndex = 0; ratingIndex < selectedParticipants.Count; ratingIndex++)
            {
                reviews.Add(new SeedClubReviewDefinition(
                    clubs[index].Definition.Slug,
                    selectedParticipants[ratingIndex],
                    $"{clubs[index].Definition.Name} review #{ratingIndex + 1}",
                    ratingPattern[ratingIndex],
                    $"Seed review for {clubs[index].Definition.Name} highlighting the welcoming {clubs[index].Definition.Tone.ToLowerInvariant()} tone.",
                    index + ratingIndex + 2));
            }
        }

        return reviews;
    }

    private static IReadOnlyList<SeedPostCommentDefinition> BuildCommentDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        var comments = new List<SeedPostCommentDefinition>();
        var participantEmails = UserSeedCatalog.Participants.Select(user => user.Email).ToList();

        for (var index = 0; index < clubs.Count; index++)
        {
            var selectedParticipants = CircularSlice(participantEmails, index * 2, 4);
            var targetedPosts = clubs[index].Posts.Take(2).ToList();

            comments.Add(new SeedPostCommentDefinition(
                clubs[index].Definition.Slug,
                targetedPosts[0].Title,
                selectedParticipants[0],
                $"Love how {clubs[index].Definition.Name} makes this easy to join for first-timers.",
                index + 1));
            comments.Add(new SeedPostCommentDefinition(
                clubs[index].Definition.Slug,
                targetedPosts[0].Title,
                selectedParticipants[1],
                $"This post answered the exact questions I had before showing up.",
                index + 2));
            comments.Add(new SeedPostCommentDefinition(
                clubs[index].Definition.Slug,
                targetedPosts[1].Title,
                selectedParticipants[2],
                $"Really appreciate the concrete details and the calm pacing here.",
                index + 3));
            comments.Add(new SeedPostCommentDefinition(
                clubs[index].Definition.Slug,
                targetedPosts[1].Title,
                selectedParticipants[3],
                $"Count me in for the next one. This feels well organized.",
                index + 4));
        }

        return comments;
    }

    private static IReadOnlyList<SeedEventImageSetDefinition> BuildImageDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        return clubs
            .SelectMany(club =>
            {
                var publicEvent = club.PublicEvents.First();
                var privateEvent = club.PrivateEvents.First();

                return new[]
                {
                    new SeedEventImageSetDefinition(
                        club.Definition.Slug,
                        publicEvent.Name,
                        new[]
                        {
                            BuildImageUrl(club.Definition.Slug, publicEvent.Name, 1),
                            BuildImageUrl(club.Definition.Slug, publicEvent.Name, 2)
                        }),
                    new SeedEventImageSetDefinition(
                        club.Definition.Slug,
                        privateEvent.Name,
                        new[]
                        {
                            BuildImageUrl(club.Definition.Slug, privateEvent.Name, 1)
                        })
                };
            })
            .ToList();
    }

    private static IReadOnlyList<SeedEventRegistrationDefinition> BuildRegistrationDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        var registrations = new List<SeedEventRegistrationDefinition>();
        var participantEmails = UserSeedCatalog.Participants.Select(user => user.Email).ToList();

        for (var index = 0; index < clubs.Count; index++)
        {
            var paidEvent = clubs[index].PublicEvents.FirstOrDefault(ev => ev.registerCost > 0);
            var targetEvents = new List<Events>();

            if (paidEvent != null)
                targetEvents.Add(paidEvent);

            targetEvents.AddRange(clubs[index].PublicEvents
                .Where(ev => paidEvent == null || ev.Id != paidEvent.Id)
                .Take(3 - targetEvents.Count));

            var eventCounts = new[] { 3, 2, 1 };
            for (var eventIndex = 0; eventIndex < targetEvents.Count; eventIndex++)
            {
                var selectedParticipants = CircularSlice(participantEmails, index + eventIndex, eventCounts[eventIndex]);
                registrations.AddRange(selectedParticipants.Select((email, offset) =>
                    new SeedEventRegistrationDefinition(
                        clubs[index].Definition.Slug,
                        targetEvents[eventIndex].Name,
                        email,
                        index + eventIndex + offset + 1)));
            }
        }

        return registrations;
    }

    private static IReadOnlyList<SeedPaymentDefinition> BuildPaymentDefinitions(
        IReadOnlyList<ResolvedSeedClub> clubs,
        IReadOnlyDictionary<string, List<SeedEventRegistrationDefinition>> registrationsByEventName)
    {
        var payments = new List<SeedPaymentDefinition>();

        foreach (var club in clubs)
        {
            var paidEvent = club.PublicEvents.FirstOrDefault(ev => ev.registerCost > 0);
            if (paidEvent == null)
                throw new InvalidOperationException($"Club '{club.Definition.Name}' must have at least one paid public event for payment seeding.");

            var key = $"{club.Definition.Slug}|{paidEvent.Name}";
            if (!registrationsByEventName.TryGetValue(key, out var registrations) || registrations.Count < 3)
            {
                throw new InvalidOperationException(
                    $"Paid event '{paidEvent.Name}' for club '{club.Definition.Name}' must have at least three registrations for payment seeding.");
            }

            payments.Add(new SeedPaymentDefinition(
                club.Definition.Slug,
                paidEvent.Name,
                registrations[0].UserEmail,
                paidEvent.registerCost,
                "usd",
                PaymentStatus.Succeeded,
                2));
            payments.Add(new SeedPaymentDefinition(
                club.Definition.Slug,
                paidEvent.Name,
                registrations[1].UserEmail,
                paidEvent.registerCost,
                "usd",
                PaymentStatus.Pending,
                3));
            payments.Add(new SeedPaymentDefinition(
                club.Definition.Slug,
                paidEvent.Name,
                registrations[2].UserEmail,
                paidEvent.registerCost,
                "usd",
                PaymentStatus.Refunded,
                4));
        }

        return payments;
    }

    private static IReadOnlyList<SeedEventInvitationLinkDefinition> BuildInvitationLinkDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        return clubs
            .Select(club => new SeedEventInvitationLinkDefinition(
                club.Definition.Slug,
                club.PrivateEvents.First().Name,
                club.Definition.OwnerEmail,
                4,
                1,
                2,
                14,
                false))
            .ToList();
    }

    private static IReadOnlyList<SeedEventInvitationDefinition> BuildInvitationDefinitions(IReadOnlyList<ResolvedSeedClub> clubs)
    {
        var invitations = new List<SeedEventInvitationDefinition>();
        var participants = UserSeedCatalog.Participants.Select(user => user.Email).ToList();

        for (var index = 0; index < clubs.Count; index++)
        {
            var privateEvent = clubs[index].PrivateEvents.First();
            var selectedParticipants = CircularSlice(participants, index, 6);

            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.DirectUser,
                EventInvitationLifecycleStatus.Pending,
                EventInvitationDeliveryStatus.Sent,
                clubs[index].Definition.ManagerEmail,
                selectedParticipants[0],
                null,
                null,
                2,
                10));
            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.DirectUser,
                EventInvitationLifecycleStatus.Accepted,
                EventInvitationDeliveryStatus.Sent,
                clubs[index].Definition.OwnerEmail,
                selectedParticipants[1],
                null,
                null,
                3,
                10));
            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.DirectEmail,
                EventInvitationLifecycleStatus.Declined,
                EventInvitationDeliveryStatus.Sent,
                clubs[index].Definition.OwnerEmail,
                selectedParticipants[2],
                selectedParticipants[2],
                null,
                4,
                10));
            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.DirectEmail,
                EventInvitationLifecycleStatus.Revoked,
                EventInvitationDeliveryStatus.Failed,
                clubs[index].Definition.ManagerEmail,
                null,
                selectedParticipants[3],
                null,
                5,
                10,
                IsRevoked: true,
                DeliveryError: "Seeded mailbox bounce"));
            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.LinkClaim,
                EventInvitationLifecycleStatus.Accepted,
                EventInvitationDeliveryStatus.Skipped,
                clubs[index].Definition.OwnerEmail,
                selectedParticipants[4],
                selectedParticipants[4],
                selectedParticipants[4],
                6,
                10,
                IsLinkBased: true));
            invitations.Add(new SeedEventInvitationDefinition(
                clubs[index].Definition.Slug,
                privateEvent.Name,
                EventInvitationSource.LinkClaim,
                EventInvitationLifecycleStatus.Declined,
                EventInvitationDeliveryStatus.Skipped,
                clubs[index].Definition.OwnerEmail,
                selectedParticipants[5],
                selectedParticipants[5],
                selectedParticipants[5],
                7,
                10,
                IsLinkBased: true));
        }

        return invitations;
    }

    private static void ReconcileClubCounters(
        IReadOnlyList<ResolvedSeedClub> clubs,
        IReadOnlyList<FollowClub> follows,
        IReadOnlyList<ClubReview> reviews)
    {
        foreach (var club in clubs.Select(entry => entry.Club))
        {
            club.MemberCount = follows.Count(follow => follow.ClubId == club.Id);

            var clubReviews = reviews
                .Where(review => review.ClubId == club.Id)
                .ToList();
            club.Rating = clubReviews.Count == 0
                ? null
                : Math.Round(clubReviews.Average(review => review.Rating), 1);
        }
    }

    private static void ReconcileEventCounters(
        IReadOnlyList<ResolvedSeedClub> clubs,
        IReadOnlyList<EventRegistration> registrations)
    {
        foreach (var ev in clubs.SelectMany(club => club.PublicEvents.Concat(club.PrivateEvents)))
            ev.RegistrationCount = registrations.Count(registration => registration.EventId == ev.Id);
    }

    private static DateTime BuildActivityTimestamp(int dayOffset)
    {
        return DateTime.UtcNow.Date.AddDays(-(dayOffset + 1)).AddHours(15).AddMinutes(dayOffset % 37);
    }

    private static IReadOnlyList<string> CircularSlice(IReadOnlyList<string> source, int startIndex, int count)
    {
        return Enumerable.Range(0, count)
            .Select(offset => source[(startIndex + offset) % source.Count])
            .ToList();
    }

    private static string BuildImageUrl(string clubSlug, string eventName, int index)
    {
        var text = Uri.EscapeDataString($"{clubSlug} {eventName} {index}");
        return $"https://placehold.co/1600x900?text={text}";
    }

    private static string ComputeStableHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string AuthRoleValue(User user) => string.IsNullOrWhiteSpace(user.Usertype) ? "Unknown" : user.Usertype.Trim();

    private static ClubVersionSnapshot BuildClubSnapshot(Club club) => new()
    {
        Name = club.Name,
        Description = club.Description,
        Clubtype = club.Clubtype.ToString(),
        ClubImage = club.ClubImage,
        Phone = club.Phone,
        Email = club.Email,
        WebsiteUrl = club.WebsiteUrl,
        Location = club.Location,
        MaxMemberCount = club.MaxMemberCount,
        IsPrivate = club.isPrivate
    };

    private static ClubVersionSnapshot BuildPreviousClubSnapshot(
        SeedClubDefinition definition,
        ClubVersionSnapshot currentSnapshot) => new()
        {
            Name = currentSnapshot.Name,
            Description = $"{definition.Name} started as a smaller pilot season for {definition.Theme.ToLowerInvariant()}.",
            Clubtype = currentSnapshot.Clubtype,
            ClubImage = currentSnapshot.ClubImage,
            Phone = currentSnapshot.Phone,
            Email = currentSnapshot.Email,
            WebsiteUrl = currentSnapshot.WebsiteUrl,
            Location = currentSnapshot.Location,
            MaxMemberCount = Math.Max(40, currentSnapshot.MaxMemberCount - 20),
            IsPrivate = currentSnapshot.IsPrivate
        };

    private static IReadOnlyList<ClubVersionFieldChange> BuildClubChangedFields(
        ClubVersionSnapshot? previous,
        ClubVersionSnapshot current)
    {
        var changes = new List<ClubVersionFieldChange>();

        AddClubChange(changes, "name", previous?.Name, current.Name);
        AddClubChange(changes, "description", previous?.Description, current.Description);
        AddClubChange(changes, "clubtype", previous?.Clubtype, current.Clubtype);
        AddClubChange(changes, "clubImage", previous?.ClubImage, current.ClubImage);
        AddClubChange(changes, "phone", previous?.Phone, current.Phone);
        AddClubChange(changes, "email", previous?.Email, current.Email);
        AddClubChange(changes, "websiteUrl", previous?.WebsiteUrl, current.WebsiteUrl);
        AddClubChange(changes, "location", previous?.Location, current.Location);
        AddClubChange(changes, "maxMemberCount", previous?.MaxMemberCount, current.MaxMemberCount);
        AddClubChange(changes, "isPrivate", previous?.IsPrivate, current.IsPrivate);

        return changes;
    }

    private static void AddClubChange(ICollection<ClubVersionFieldChange> changes, string field, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;

        changes.Add(new ClubVersionFieldChange
        {
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static void AddClubChange(ICollection<ClubVersionFieldChange> changes, string field, int? oldValue, int newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new ClubVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static void AddClubChange(ICollection<ClubVersionFieldChange> changes, string field, bool? oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new ClubVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString()?.ToLowerInvariant(),
            NewValue = newValue.ToString().ToLowerInvariant()
        });
    }

    private static EventVersionSnapshot BuildEventSnapshot(Events ev) => new()
    {
        Name = ev.Name,
        Description = ev.Description,
        Location = ev.Location,
        IsPrivate = ev.isPrivate,
        MaxParticipants = ev.maxParticipants,
        RegisterCost = ev.registerCost,
        StartTime = ev.StartTime,
        EndTime = ev.EndTime,
        ClubId = ev.ClubId,
        Category = ev.Category,
        VenueName = ev.VenueName,
        City = ev.City,
        Latitude = ev.Latitude,
        Longitude = ev.Longitude,
        Tags = ev.Tags.ToList()
    };

    private static EventVersionSnapshot BuildPreviousEventSnapshot(EventVersionSnapshot currentSnapshot) => new()
    {
        Name = currentSnapshot.Name,
        Description = $"{currentSnapshot.Description} This earlier pilot version ran with a smaller cohort.",
        Location = currentSnapshot.Location,
        IsPrivate = currentSnapshot.IsPrivate,
        MaxParticipants = Math.Max(12, currentSnapshot.MaxParticipants - 8),
        RegisterCost = currentSnapshot.RegisterCost,
        StartTime = currentSnapshot.StartTime,
        EndTime = currentSnapshot.EndTime,
        ClubId = currentSnapshot.ClubId,
        Category = currentSnapshot.Category,
        VenueName = currentSnapshot.VenueName,
        City = currentSnapshot.City,
        Latitude = currentSnapshot.Latitude,
        Longitude = currentSnapshot.Longitude,
        Tags = currentSnapshot.Tags.ToList()
    };

    private static IReadOnlyList<EventVersionFieldChange> BuildEventChangedFields(
        EventVersionSnapshot? previous,
        EventVersionSnapshot current)
    {
        var changes = new List<EventVersionFieldChange>();

        AddEventChange(changes, "name", previous?.Name, current.Name);
        AddEventChange(changes, "description", previous?.Description, current.Description);
        AddEventChange(changes, "location", previous?.Location, current.Location);
        AddEventChange(changes, "isPrivate", previous?.IsPrivate, current.IsPrivate);
        AddEventChange(changes, "maxParticipants", previous?.MaxParticipants, current.MaxParticipants);
        AddEventChange(changes, "registerCost", previous?.RegisterCost, current.RegisterCost);
        AddEventChange(changes, "startTime", previous?.StartTime, current.StartTime);
        AddEventChange(changes, "endTime", previous?.EndTime, current.EndTime);
        AddEventChange(changes, "clubId", previous?.ClubId, current.ClubId);
        AddEventChange(changes, "category", previous?.Category, current.Category);
        AddEventChange(changes, "venueName", previous?.VenueName, current.VenueName);
        AddEventChange(changes, "city", previous?.City, current.City);
        AddEventChange(changes, "latitude", previous?.Latitude, current.Latitude);
        AddEventChange(changes, "longitude", previous?.Longitude, current.Longitude);
        AddEventChange(changes, "tags", previous?.Tags, current.Tags);

        return changes;
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, bool? oldValue, bool newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString()?.ToLowerInvariant(),
            NewValue = newValue.ToString().ToLowerInvariant()
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, int? oldValue, int newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, DateTime? oldValue, DateTime? newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString("O", CultureInfo.InvariantCulture),
            NewValue = newValue?.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, EventCategory? oldValue, EventCategory newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(),
            NewValue = newValue.ToString()
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, double? oldValue, double? newValue)
    {
        if (oldValue == newValue)
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = oldValue?.ToString(CultureInfo.InvariantCulture),
            NewValue = newValue?.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static void AddEventChange(ICollection<EventVersionFieldChange> changes, string field, IReadOnlyList<string>? oldValue, IReadOnlyList<string> newValue)
    {
        var previousTags = oldValue?.ToList() ?? [];
        var currentTags = newValue.ToList();

        if (previousTags.SequenceEqual(currentTags, StringComparer.Ordinal))
            return;

        changes.Add(new EventVersionFieldChange
        {
            Field = field,
            OldValue = JsonSerializer.Serialize(previousTags),
            NewValue = JsonSerializer.Serialize(currentTags)
        });
    }

    private sealed record ResolvedSeedClub(
        SeedClubDefinition Definition,
        Club Club,
        IReadOnlyList<Events> PublicEvents,
        IReadOnlyList<Events> PrivateEvents,
        IReadOnlyList<ClubPost> Posts);
}
