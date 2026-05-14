using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.seeders;

public sealed class SeedClubContentSeeder : ISeeder
{
    private readonly AppDatabaseContext _dbContext;
    private readonly IEventSearchOutboxWriter _eventOutboxWriter;
    private readonly IClubPostSearchOutboxWriter _clubPostOutboxWriter;
    private readonly IClubSearchOutboxWriter _clubOutboxWriter;
    private readonly IEnumerable<IClubSeedDefinitionSource> _clubSeedSources;
    private readonly ILogger<SeedClubContentSeeder> _logger;

    public SeedClubContentSeeder(
        AppDatabaseContext dbContext,
        IEventSearchOutboxWriter eventOutboxWriter,
        IClubPostSearchOutboxWriter clubPostOutboxWriter,
        IClubSearchOutboxWriter clubOutboxWriter,
        IEnumerable<IClubSeedDefinitionSource> clubSeedSources,
        ILogger<SeedClubContentSeeder> logger)
    {
        _dbContext = dbContext;
        _eventOutboxWriter = eventOutboxWriter;
        _clubPostOutboxWriter = clubPostOutboxWriter;
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

        if (clubDefinitions.Count != 10)
            throw new InvalidOperationException(
                $"Expected 10 themed club seed definitions, but found {clubDefinitions.Count}.");

        var seasonStartUtc = DateTime.UtcNow.Date.AddDays(5).AddHours(16);
        var desiredUserEmails = clubDefinitions
            .SelectMany(GetRequiredUserEmails)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var users = await _dbContext.Users
            .Where(user => desiredUserEmails.Contains(user.Email))
            .ToListAsync(cancellationToken);
        var usersByEmail = users.ToDictionary(user => user.Email, StringComparer.OrdinalIgnoreCase);
        var missingUsers = desiredUserEmails.Where(email => !usersByEmail.ContainsKey(email)).ToList();

        if (missingUsers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Club seed reconciliation requires seed users to exist first. Missing users: {string.Join(", ", missingUsers)}");
        }

        var seededClubs = await _dbContext.Clubs
            .Where(IsSeedClubPredicate())
            .ToListAsync(cancellationToken);
        var seededClubsByName = seededClubs.ToDictionary(club => club.Name, StringComparer.OrdinalIgnoreCase);
        var desiredClubNames = clubDefinitions
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var touchedClubs = new List<Club>();
        foreach (var definition in clubDefinitions)
        {
            if (!usersByEmail.TryGetValue(definition.OwnerEmail, out var owner))
                throw new InvalidOperationException($"Owner user '{definition.OwnerEmail}' was not found.");

            var club = seededClubsByName.TryGetValue(definition.Name, out var existing)
                ? existing
                : CreateClub(definition, owner.Id);

            var changed = existing == null
                ? true
                : ApplyDefinition(club, definition, owner.Id);

            if (existing == null)
                _dbContext.Clubs.Add(club);

            if (changed)
                touchedClubs.Add(club);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var currentClubs = await _dbContext.Clubs
            .Where(club => desiredClubNames.Contains(club.Name))
            .ToListAsync(cancellationToken);
        var currentClubsByName = currentClubs.ToDictionary(club => club.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var touchedClub in touchedClubs)
            _clubOutboxWriter.StageUpsert(touchedClub);

        var reconciledEventCount = 0;
        var touchedEventCount = 0;
        var removedEventCount = 0;
        var reconciledPostCount = 0;
        var touchedPostCount = 0;
        var removedPostCount = 0;

        foreach (var definition in clubDefinitions)
        {
            var club = currentClubsByName[definition.Name];
            await ReconcileStaffAsync(club, definition, usersByEmail, cancellationToken);

            var eventChanges = await ReconcileEventsAsync(
                club,
                definition,
                seasonStartUtc,
                cancellationToken);

            var postChanges = await ReconcilePostsAsync(
                club,
                definition,
                seasonStartUtc,
                usersByEmail,
                cancellationToken);

            reconciledEventCount += eventChanges.TotalEvents;
            touchedEventCount += eventChanges.UpsertedOrUpdatedCount;
            removedEventCount += eventChanges.RemovedCount;
            reconciledPostCount += postChanges.TotalPosts;
            touchedPostCount += postChanges.UpsertedOrUpdatedCount;
            removedPostCount += postChanges.RemovedCount;
        }

        var staleSeedClubs = seededClubs
            .Where(club => !desiredClubNames.Contains(club.Name))
            .ToList();

        foreach (var staleClub in staleSeedClubs)
        {
            var removalCounts = await RemoveClubAsync(staleClub, cancellationToken);
            removedEventCount += removalCounts.RemovedEventCount;
            removedPostCount += removalCounts.RemovedPostCount;
        }

        await RefreshClubEventCountsAsync(currentClubs.Select(club => club.Id).ToList(), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await CleanupStaleSeedUsersAsync(desiredUserEmails.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Seeders] Reconciled themed clubs. Clubs: {ClubCount}, events: {EventCount}, posts: {PostCount}, touched events: {TouchedEventCount}, removed events: {RemovedEventCount}, touched posts: {TouchedPostCount}, removed posts: {RemovedPostCount}.",
            currentClubs.Count,
            reconciledEventCount,
            reconciledPostCount,
            touchedEventCount,
            removedEventCount,
            touchedPostCount,
            removedPostCount);
    }

    private static IEnumerable<string> GetRequiredUserEmails(SeedClubDefinition definition)
    {
        yield return definition.OwnerEmail;
        yield return definition.ManagerEmail;
        yield return definition.VolunteerEmail;
    }

    private static Club CreateClub(SeedClubDefinition definition, int ownerUserId)
    {
        var now = DateTime.UtcNow;

        return new Club
        {
            Name = definition.Name,
            Description = definition.Description,
            Clubtype = definition.ClubType,
            ClubImage = definition.ClubImage,
            Phone = definition.Phone,
            Email = definition.Email,
            WebsiteUrl = definition.WebsiteUrl,
            Location = definition.Location,
            MaxMemberCount = definition.MaxMemberCount,
            isPrivate = false,
            UserId = ownerUserId,
            CurrentVersionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static bool ApplyDefinition(Club club, SeedClubDefinition definition, int ownerUserId)
    {
        var changed = false;

        changed |= SetIfDifferent(club.Name, definition.Name, value => club.Name = value);
        changed |= SetIfDifferent(club.Description, definition.Description, value => club.Description = value);

        if (club.Clubtype != definition.ClubType)
        {
            club.Clubtype = definition.ClubType;
            changed = true;
        }

        changed |= SetIfDifferent(club.ClubImage, definition.ClubImage, value => club.ClubImage = value);
        changed |= SetIfDifferent(club.Phone, definition.Phone, value => club.Phone = value);
        changed |= SetIfDifferent(club.Email, definition.Email, value => club.Email = value);
        changed |= SetIfDifferent(club.WebsiteUrl, definition.WebsiteUrl, value => club.WebsiteUrl = value);
        changed |= SetIfDifferent(club.Location, definition.Location, value => club.Location = value);

        if (club.MaxMemberCount != definition.MaxMemberCount)
        {
            club.MaxMemberCount = definition.MaxMemberCount;
            changed = true;
        }

        if (club.isPrivate)
        {
            club.isPrivate = false;
            changed = true;
        }

        if (club.UserId != ownerUserId)
        {
            club.UserId = ownerUserId;
            changed = true;
        }

        if (club.CurrentVersionNumber < 1)
        {
            club.CurrentVersionNumber = 1;
            changed = true;
        }

        if (changed)
            club.UpdatedAt = DateTime.UtcNow;

        return changed;
    }

    private async Task ReconcileStaffAsync(
        Club club,
        SeedClubDefinition definition,
        IReadOnlyDictionary<string, User> usersByEmail,
        CancellationToken cancellationToken)
    {
        var desiredAssignments = new Dictionary<int, ClubStaffRole>
        {
            [usersByEmail[definition.ManagerEmail].Id] = ClubStaffRole.Manager,
            [usersByEmail[definition.VolunteerEmail].Id] = ClubStaffRole.Volunteer
        };

        var existingStaff = await _dbContext.ClubStaff
            .Where(staff => staff.ClubId == club.Id)
            .ToListAsync(cancellationToken);

        foreach (var assignment in existingStaff.ToList())
        {
            if (assignment.UserId == club.UserId)
            {
                _dbContext.ClubStaff.Remove(assignment);
                continue;
            }

            if (!desiredAssignments.TryGetValue(assignment.UserId, out var desiredRole))
            {
                _dbContext.ClubStaff.Remove(assignment);
                continue;
            }

            if (assignment.Role != desiredRole)
            {
                assignment.Role = desiredRole;
                assignment.UpdatedAt = DateTime.UtcNow;
            }
        }

        var remainingStaffUserIds = existingStaff
            .Where(staff => _dbContext.Entry(staff).State != EntityState.Deleted)
            .Select(staff => staff.UserId)
            .ToHashSet();

        foreach (var desiredAssignment in desiredAssignments)
        {
            if (remainingStaffUserIds.Contains(desiredAssignment.Key))
                continue;

            var now = DateTime.UtcNow;

            _dbContext.ClubStaff.Add(new ClubStaff
            {
                ClubId = club.Id,
                UserId = desiredAssignment.Key,
                Role = desiredAssignment.Value,
                GrantedByUserId = club.UserId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task<(int TotalEvents, int UpsertedOrUpdatedCount, int RemovedCount)> ReconcileEventsAsync(
        Club club,
        SeedClubDefinition definition,
        DateTime seasonStartUtc,
        CancellationToken cancellationToken)
    {
        var desiredEvents = ThematicEventFactory.BuildEvents(definition, seasonStartUtc);
        var desiredByName = desiredEvents.ToDictionary(ev => ev.Name, StringComparer.OrdinalIgnoreCase);

        var existingEvents = await _dbContext.Events
            .Where(ev => ev.ClubId == club.Id)
            .ToListAsync(cancellationToken);
        var managedExistingEvents = existingEvents
            .Where(ev => IsManagedSeedEvent(ev, definition.Slug))
            .ToDictionary(ev => ev.Name, StringComparer.OrdinalIgnoreCase);

        var upsertedOrUpdatedCount = 0;

        foreach (var desiredEvent in desiredEvents)
        {
            if (managedExistingEvents.TryGetValue(desiredEvent.Name, out var existing))
            {
                if (ApplyEventDefinition(existing, desiredEvent, club.Id))
                {
                    _eventOutboxWriter.StageUpsert(existing);
                    upsertedOrUpdatedCount++;
                }

                continue;
            }

            var created = CreateEvent(club.Id, desiredEvent);
            _dbContext.Events.Add(created);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _eventOutboxWriter.StageUpsert(created);
            upsertedOrUpdatedCount++;
        }

        var removedCount = 0;

        foreach (var managedEvent in managedExistingEvents.Values)
        {
            if (desiredByName.ContainsKey(managedEvent.Name))
                continue;

            _eventOutboxWriter.StageDelete(managedEvent.Id);
            _dbContext.Events.Remove(managedEvent);
            removedCount++;
        }

        return (desiredEvents.Count, upsertedOrUpdatedCount, removedCount);
    }

    private async Task<(int TotalPosts, int UpsertedOrUpdatedCount, int RemovedCount)> ReconcilePostsAsync(
        Club club,
        SeedClubDefinition definition,
        DateTime seasonStartUtc,
        IReadOnlyDictionary<string, User> usersByEmail,
        CancellationToken cancellationToken)
    {
        var desiredPosts = ThematicClubPostFactory.BuildPosts(definition, seasonStartUtc);
        var desiredByTitle = desiredPosts.ToDictionary(post => post.Title, StringComparer.OrdinalIgnoreCase);
        var seedAuthorIds = new[]
        {
            usersByEmail[definition.OwnerEmail].Id,
            usersByEmail[definition.ManagerEmail].Id,
            usersByEmail[definition.VolunteerEmail].Id
        }.ToHashSet();

        var existingPosts = await _dbContext.ClubPosts
            .Where(post => post.ClubId == club.Id)
            .ToListAsync(cancellationToken);
        var managedExistingPosts = existingPosts
            .Where(post => seedAuthorIds.Contains(post.UserId))
            .ToDictionary(post => post.Title, StringComparer.OrdinalIgnoreCase);

        var upsertedOrUpdatedCount = 0;

        foreach (var desiredPost in desiredPosts)
        {
            var authorId = ResolveAuthorId(desiredPost.AuthorRole, definition, usersByEmail);

            if (managedExistingPosts.TryGetValue(desiredPost.Title, out var existing))
            {
                if (ApplyPostDefinition(existing, desiredPost, club.Id, authorId))
                {
                    _clubPostOutboxWriter.StageUpsert(existing);
                    upsertedOrUpdatedCount++;
                }

                continue;
            }

            var created = CreatePost(club.Id, authorId, desiredPost);
            _dbContext.ClubPosts.Add(created);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _clubPostOutboxWriter.StageUpsert(created);
            upsertedOrUpdatedCount++;
        }

        var removedCount = 0;

        foreach (var managedPost in managedExistingPosts.Values)
        {
            if (desiredByTitle.ContainsKey(managedPost.Title))
                continue;

            _clubPostOutboxWriter.StageDelete(managedPost.Id);
            _dbContext.ClubPosts.Remove(managedPost);
            removedCount++;
        }

        return (desiredPosts.Count, upsertedOrUpdatedCount, removedCount);
    }

    private async Task<(int RemovedEventCount, int RemovedPostCount)> RemoveClubAsync(Club staleClub, CancellationToken cancellationToken)
    {
        var clubEvents = await _dbContext.Events
            .Where(ev => ev.ClubId == staleClub.Id)
            .ToListAsync(cancellationToken);
        var clubPosts = await _dbContext.ClubPosts
            .Where(post => post.ClubId == staleClub.Id)
            .ToListAsync(cancellationToken);

        foreach (var clubEvent in clubEvents)
            _eventOutboxWriter.StageDelete(clubEvent.Id);
        foreach (var clubPost in clubPosts)
            _clubPostOutboxWriter.StageDelete(clubPost.Id);

        _clubOutboxWriter.StageDelete(staleClub.Id);
        _dbContext.Clubs.Remove(staleClub);

        return (clubEvents.Count, clubPosts.Count);
    }

    private async Task RefreshClubEventCountsAsync(
        IReadOnlyCollection<int> clubIds,
        CancellationToken cancellationToken)
    {
        if (clubIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var counts = await _dbContext.Events
            .Where(ev => clubIds.Contains(ev.ClubId))
            .GroupBy(ev => ev.ClubId)
            .Select(group => new
            {
                ClubId = group.Key,
                EventCount = group.Count(),
                AvailableEventCount = group.Count(ev => ev.EndTime == null || ev.EndTime > now)
            })
            .ToListAsync(cancellationToken);

        var countLookup = counts.ToDictionary(item => item.ClubId);
        var clubs = await _dbContext.Clubs
            .Where(club => clubIds.Contains(club.Id))
            .ToListAsync(cancellationToken);

        foreach (var club in clubs)
        {
            if (countLookup.TryGetValue(club.Id, out var value))
            {
                club.EventCount = value.EventCount;
                club.AvaliableEventCount = value.AvailableEventCount;
            }
            else
            {
                club.EventCount = 0;
                club.AvaliableEventCount = 0;
            }

            club.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task CleanupStaleSeedUsersAsync(
        IReadOnlySet<string> desiredUserEmails,
        CancellationToken cancellationToken)
    {
        var staleSeedUsers = await _dbContext.Users
            .Where(user =>
                user.Email.EndsWith(SeedCatalogConstants.SeedEmailDomain) &&
                !desiredUserEmails.Contains(user.Email))
            .ToListAsync(cancellationToken);

        foreach (var staleUser in staleSeedUsers)
        {
            var ownsClub = await _dbContext.Clubs
                .AnyAsync(club => club.UserId == staleUser.Id, cancellationToken);
            var hasStaffAssignment = await _dbContext.ClubStaff
                .AnyAsync(staff => staff.UserId == staleUser.Id || staff.GrantedByUserId == staleUser.Id, cancellationToken);

            if (ownsClub || hasStaffAssignment)
                continue;

            _dbContext.Users.Remove(staleUser);
        }
    }

    private static Events CreateEvent(int clubId, SeedEventDefinition definition)
    {
        return new Events
        {
            Name = definition.Name,
            Description = definition.Description,
            Location = definition.Location,
            isPrivate = definition.IsPrivate,
            maxParticipants = definition.MaxParticipants,
            registerCost = definition.RegisterCost,
            StartTime = definition.StartTimeUtc,
            EndTime = definition.EndTimeUtc,
            ClubId = clubId,
            CurrentVersionNumber = 1,
            CreatedAt = definition.CreatedAtUtc,
            UpdatedAt = definition.UpdatedAtUtc,
            Category = definition.Category,
            VenueName = definition.VenueName,
            City = definition.City,
            Latitude = definition.Latitude,
            Longitude = definition.Longitude,
            Tags = definition.Tags.ToList()
        };
    }

    private static ClubPost CreatePost(int clubId, int userId, ResolvedSeedClubPostDefinition definition)
    {
        return new ClubPost
        {
            ClubId = clubId,
            UserId = userId,
            Title = definition.Title,
            Content = definition.Content,
            PostType = definition.PostType,
            LikesCount = definition.LikesCount,
            ViewCount = definition.ViewCount,
            IsPinned = definition.IsPinned,
            CreatedAt = definition.CreatedAtUtc,
            UpdatedAt = definition.UpdatedAtUtc
        };
    }

    private static bool ApplyEventDefinition(Events existing, SeedEventDefinition definition, int clubId)
    {
        var changed = false;

        changed |= SetIfDifferent(existing.Name, definition.Name, value => existing.Name = value);
        changed |= SetIfDifferent(existing.Description, definition.Description, value => existing.Description = value);
        changed |= SetIfDifferent(existing.Location, definition.Location, value => existing.Location = value);

        if (existing.isPrivate != definition.IsPrivate)
        {
            existing.isPrivate = definition.IsPrivate;
            changed = true;
        }

        if (existing.maxParticipants != definition.MaxParticipants)
        {
            existing.maxParticipants = definition.MaxParticipants;
            changed = true;
        }

        if (existing.registerCost != definition.RegisterCost)
        {
            existing.registerCost = definition.RegisterCost;
            changed = true;
        }

        if (existing.StartTime != definition.StartTimeUtc)
        {
            existing.StartTime = definition.StartTimeUtc;
            changed = true;
        }

        if (existing.EndTime != definition.EndTimeUtc)
        {
            existing.EndTime = definition.EndTimeUtc;
            changed = true;
        }

        if (existing.ClubId != clubId)
        {
            existing.ClubId = clubId;
            changed = true;
        }

        if (existing.Category != definition.Category)
        {
            existing.Category = definition.Category;
            changed = true;
        }

        changed |= SetIfDifferent(existing.VenueName, definition.VenueName, value => existing.VenueName = value);
        changed |= SetIfDifferent(existing.City, definition.City, value => existing.City = value);

        if (existing.Latitude != definition.Latitude)
        {
            existing.Latitude = definition.Latitude;
            changed = true;
        }

        if (existing.Longitude != definition.Longitude)
        {
            existing.Longitude = definition.Longitude;
            changed = true;
        }

        if (!existing.Tags.SequenceEqual(definition.Tags))
        {
            existing.Tags = definition.Tags.ToList();
            changed = true;
        }

        if (existing.CurrentVersionNumber < 1)
        {
            existing.CurrentVersionNumber = 1;
            changed = true;
        }

        if (existing.CreatedAt != definition.CreatedAtUtc)
        {
            existing.CreatedAt = definition.CreatedAtUtc;
            changed = true;
        }

        if (existing.UpdatedAt != definition.UpdatedAtUtc)
        {
            existing.UpdatedAt = definition.UpdatedAtUtc;
            changed = true;
        }

        return changed;
    }

    private static bool ApplyPostDefinition(
        ClubPost existing,
        ResolvedSeedClubPostDefinition definition,
        int clubId,
        int authorId)
    {
        var changed = false;

        if (existing.ClubId != clubId)
        {
            existing.ClubId = clubId;
            changed = true;
        }

        if (existing.UserId != authorId)
        {
            existing.UserId = authorId;
            changed = true;
        }

        changed |= SetIfDifferent(existing.Title, definition.Title, value => existing.Title = value ?? string.Empty);
        changed |= SetIfDifferent(existing.Content, definition.Content, value => existing.Content = value ?? string.Empty);

        if (existing.PostType != definition.PostType)
        {
            existing.PostType = definition.PostType;
            changed = true;
        }

        if (existing.IsPinned != definition.IsPinned)
        {
            existing.IsPinned = definition.IsPinned;
            changed = true;
        }

        if (existing.LikesCount != definition.LikesCount)
        {
            existing.LikesCount = definition.LikesCount;
            changed = true;
        }

        if (existing.ViewCount != definition.ViewCount)
        {
            existing.ViewCount = definition.ViewCount;
            changed = true;
        }

        if (existing.CreatedAt != definition.CreatedAtUtc)
        {
            existing.CreatedAt = definition.CreatedAtUtc;
            changed = true;
        }

        if (existing.UpdatedAt != definition.UpdatedAtUtc)
        {
            existing.UpdatedAt = definition.UpdatedAtUtc;
            changed = true;
        }

        return changed;
    }

    private static int ResolveAuthorId(
        SeedClubAuthorRole authorRole,
        SeedClubDefinition definition,
        IReadOnlyDictionary<string, User> usersByEmail)
    {
        return authorRole switch
        {
            SeedClubAuthorRole.Owner => usersByEmail[definition.OwnerEmail].Id,
            SeedClubAuthorRole.Manager => usersByEmail[definition.ManagerEmail].Id,
            SeedClubAuthorRole.Volunteer => usersByEmail[definition.VolunteerEmail].Id,
            _ => throw new InvalidOperationException($"Unsupported seeded author role '{authorRole}'.")
        };
    }

    private static bool IsManagedSeedEvent(Events ev, string clubSlug)
    {
        return ev.Tags.Contains(SeedCatalogConstants.SeedEventTag, StringComparer.Ordinal)
            && ev.Tags.Contains(SeedCatalogConstants.ClubSeedTag(clubSlug), StringComparer.Ordinal);
    }

    private static System.Linq.Expressions.Expression<Func<Club, bool>> IsSeedClubPredicate()
    {
        return club =>
            (club.Email != null && EF.Functions.Like(club.Email, $"%{SeedCatalogConstants.SeedEmailDomain}"))
            || (club.WebsiteUrl != null && EF.Functions.Like(club.WebsiteUrl, $"%{SeedCatalogConstants.SeedWebsiteHost}%"));
    }

    private static bool SetIfDifferent(string? currentValue, string? nextValue, Action<string?> setter)
    {
        if (string.Equals(currentValue, nextValue, StringComparison.Ordinal))
            return false;

        setter(nextValue);
        return true;
    }
}
