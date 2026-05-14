using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs.contracts;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.versions;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace backend.main.features.clubs
{
    public class ClubService : IClubService
    {
        private readonly AppDatabaseContext _db;
        private readonly IClubRepository _clubRepository;
        private readonly IUserService _userService;
        private readonly IFollowService _followService;
        private readonly IFileUploadService _fileUploadService;
        private readonly ICacheService _cache;
        private readonly ClubVersioningOptions _versioningOptions;
        private readonly TimeProvider _timeProvider;

        private static readonly TimeSpan ClubTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ClubListTTL = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);
        private const int HotThreshold = 5;
        private static readonly TimeSpan HotWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HotCooldown = TimeSpan.FromMinutes(2);
        private const string ClubListVersionKey = "clubs:version";
        private const string NullSentinel = "__null__";

        public ClubService(
            AppDatabaseContext db,
            IClubRepository clubRepository,
            IUserService userService,
            IFileUploadService fileUploadService,
            IFollowService followService,
            ICacheService cache,
            IOptions<ClubVersioningOptions> versioningOptions,
            TimeProvider timeProvider)
        {
            _db = db;
            _clubRepository = clubRepository;
            _followService = followService;
            _userService = userService;
            _fileUploadService = fileUploadService;
            _cache = cache;
            _versioningOptions = versioningOptions.Value;
            _timeProvider = timeProvider;
        }

        public async Task<Club> CreateClub(
            string name,
            int userId,
            string description,
            string clubtype,
            IFormFile clubimage,
            string? phone = null,
            string? email = null)
        {
            var user = await _userService.GetUserByIdAsync(userId)
                ?? throw new ResourceNotFoundException("User not found");

            var imageUrl = await _fileUploadService.UploadImageAsync(clubimage, "clubs")
                ?? throw new InternalServerErrorException("Image upload failed");

            var now = GetUtcNow();
            var club = new Club
            {
                Name = name,
                Description = description,
                Clubtype = ParseClubType(clubtype),
                ClubImage = imageUrl,
                Phone = phone,
                Email = email,
                UserId = userId,
                CurrentVersionNumber = 1,
                CreatedAt = now,
                UpdatedAt = now
            };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.Clubs.Add(club);
            await _db.SaveChangesAsync();

            AddVersionRecord(
                club,
                ClubVersionActions.Create,
                actorUserId: userId,
                actorRole: NormalizeActorRole(user.Usertype),
                rollbackSourceVersionNumber: null,
                changedFields: BuildChangedFields(null, BuildSnapshot(club)),
                createdAt: now);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await CacheClubAsync(club);
            await BumpClubListVersionAsync();

            return club;
        }

        public async Task<Club> GetClub(int clubId)
        {
            var key = GetClubCacheKey(clubId);

            var cached = await _cache.GetValueAsync(key);

            if (cached == NullSentinel)
                throw new ResourceNotFoundException($"Club {clubId} not found");

            Club club;

            if (cached != null)
            {
                var dto = JsonSerializer.Deserialize<ClubCacheDto>(cached)!;
                club = ClubCacheMapper.ToEntity(dto);
            }
            else
            {
                var fetchedClub = await _clubRepository.GetByIdAsync(clubId);
                if (fetchedClub == null)
                {
                    await _cache.SetValueAsync(key, NullSentinel, NotFoundTTL);
                    throw new ResourceNotFoundException($"Club {clubId} not found");
                }

                club = fetchedClub;
                await CacheClubAsync(club);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await TrackClubAccessAsync(clubId);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to track club access for club");
                }
            });

            return club;
        }

        public async Task<Club> GetClubByUser(int userId)
        {
            var key = GetClubUserCacheKey(userId);

            var cached = await _cache.GetValueAsync(key);

            if (cached == NullSentinel)
                throw new ResourceNotFoundException($"Club for user {userId} not found");

            Club club;

            if (cached != null)
            {
                var dto = JsonSerializer.Deserialize<ClubCacheDto>(cached)!;
                club = ClubCacheMapper.ToEntity(dto);
            }
            else
            {
                var fetchedClub = await _clubRepository.GetByUserIdAsync(userId);
                if (fetchedClub == null)
                {
                    await _cache.SetValueAsync(key, NullSentinel, NotFoundTTL);
                    throw new ResourceNotFoundException($"Club for user {userId} not found");
                }

                club = fetchedClub;
                await CacheClubAsync(club);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await TrackClubAccessAsync(club.Id);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to track club access for club");
                }
            });

            return club;
        }

        public async Task<List<Club>> GetAllClubs(
            string? search = null,
            int page = 1,
            int pageSize = 20)
        {
            var version = await GetClubListVersionAsync();

            var normalizedSearch = search?.Trim().ToLowerInvariant();

            var key = normalizedSearch == null
                ? $"clubs:list:v{version}:p{page}:s{pageSize}"
                : $"clubs:list:v{version}:q:{normalizedSearch}:p{page}:s{pageSize}";

            var cached = await _cache.GetValueAsync(key);
            if (cached != null)
            {
                var dtos = JsonSerializer.Deserialize<List<ClubCacheDto>>(cached)!;
                return dtos.Select(ClubCacheMapper.ToEntity).ToList();
            }

            var clubs = await _clubRepository.SearchAsync(
                normalizedSearch,
                page,
                pageSize
            );

            var dtoList = clubs.Select(ClubCacheMapper.ToDto).ToList();

            await _cache.SetValueAsync(
                key,
                JsonSerializer.Serialize(dtoList),
                WithJitter(ClubListTTL)
            );

            return clubs;
        }

        public async Task<List<Club>> GetClubsByIdsAsync(IEnumerable<int> clubIds)
        {
            var ids = clubIds.Distinct().ToList();

            var cacheKeys = ids.Select(GetClubCacheKey).ToList();
            var cached = await _cache.GetManyAsync(cacheKeys);

            var results = new List<Club>();
            var missingIds = new List<int>();

            foreach (var id in ids)
            {
                var key = GetClubCacheKey(id);
                if (cached.TryGetValue(key, out var value) && value != null && value != NullSentinel)
                {
                    var dto = JsonSerializer.Deserialize<ClubCacheDto>(value)!;
                    results.Add(ClubCacheMapper.ToEntity(dto));
                }
                else
                {
                    missingIds.Add(id);
                }
            }

            if (missingIds.Any())
            {
                var fetched = await _clubRepository.GetByIdsAsync(missingIds);

                foreach (var club in fetched)
                    await CacheClubAsync(club);

                results.AddRange(fetched);
            }

            return results;
        }

        public async Task<Club> UpdateClub(
            int clubId,
            int userId,
            string userRole,
            string name,
            string description,
            string clubtype,
            IFormFile clubimage,
            string? phone = null,
            string? email = null)
        {
            var existing = await GetTrackedClubOrThrowAsync(clubId);
            EnsureOwnerOrAdmin(existing, userId, userRole);

            var previousSnapshot = BuildSnapshot(existing);

            var newImage = await _fileUploadService.UploadImageAsync(clubimage, "clubs")
                ?? throw new InternalServerErrorException("Image upload failed");

            existing.Name = name;
            existing.Description = description;
            existing.Clubtype = ParseClubType(clubtype);
            existing.ClubImage = newImage;
            existing.Phone = phone;
            existing.Email = email;
            existing.CurrentVersionNumber += 1;
            existing.UpdatedAt = GetUtcNow();

            var newSnapshot = BuildSnapshot(existing);
            var changedFields = BuildChangedFields(previousSnapshot, newSnapshot);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            await _db.SaveChangesAsync();

            AddVersionRecord(
                existing,
                ClubVersionActions.Update,
                actorUserId: userId,
                actorRole: NormalizeActorRole(userRole),
                rollbackSourceVersionNumber: null,
                changedFields: changedFields,
                createdAt: existing.UpdatedAt);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await CacheClubAsync(existing);
            await BumpClubListVersionAsync();

            return existing;
        }

        public async Task DeleteClub(int clubId, int userId)
        {
            var club = await GetTrackedClubOrThrowAsync(clubId);

            if (club.UserId != userId)
                throw new ForbiddenException("Not allowed");

            await _fileUploadService.DeleteImageAsync(club.ClubImage);

            _db.Clubs.Remove(club);
            await _db.SaveChangesAsync();

            await _cache.DeleteKeyAsync(GetClubCacheKey(clubId));
            await _cache.DeleteKeyAsync(GetClubUserCacheKey(club.UserId));
            await BumpClubListVersionAsync();
        }

        public async Task JoinClubAsync(int clubId, int userId)
        {
            var club = await _clubRepository.GetByIdAsync(clubId)
                ?? throw new ResourceNotFoundException("Club not found");

            if (await _followService.IsMemberAsync(clubId, userId))
                throw new ConflictException("Already a member");

            await _followService.AddMembershipAsync(clubId, userId);

            club.MemberCount++;
            await _clubRepository.UpdateAsync(clubId, club);

            await CacheClubAsync(club);
            await BumpClubListVersionAsync();
        }

        public async Task LeaveClubAsync(int clubId, int userId)
        {
            var club = await _clubRepository.GetByIdAsync(clubId)
                ?? throw new ResourceNotFoundException("Club not found");

            if (!await _followService.IsMemberAsync(clubId, userId))
                throw new ConflictException("Not a member");

            await _followService.RemoveMembershipAsync(clubId, userId);

            club.MemberCount--;
            await _clubRepository.UpdateAsync(clubId, club);

            await CacheClubAsync(club);
            await BumpClubListVersionAsync();
        }

        public async Task<(List<ClubVersionHistoryItem> Items, int TotalCount)> GetVersionHistoryAsync(
            int clubId,
            int userId,
            string userRole,
            int page = 1,
            int pageSize = 20)
        {
            page = NormalizePage(page);
            pageSize = NormalizePageSize(pageSize);

            var club = await GetClubRecordOrThrowAsync(clubId);
            EnsureOwnerOrAdmin(club, userId, userRole);

            var query = _db.ClubVersions
                .AsNoTracking()
                .Where(v => v.ClubId == clubId);

            var totalCount = await query.CountAsync();
            var versions = await query
                .OrderByDescending(v => v.VersionNumber)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (
                versions.Select(v => MapHistoryItem(v, club.CurrentVersionNumber)).ToList(),
                totalCount
            );
        }

        public async Task<ClubVersionDetail> GetVersionDetailAsync(
            int clubId,
            int versionNumber,
            int userId,
            string userRole)
        {
            var club = await GetClubRecordOrThrowAsync(clubId);
            EnsureOwnerOrAdmin(club, userId, userRole);

            var version = await _db.ClubVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.ClubId == clubId && v.VersionNumber == versionNumber)
                ?? throw new ResourceNotFoundException(
                    $"Version {versionNumber} for club {clubId} was not found.");

            return new ClubVersionDetail(
                version.ClubId,
                version.VersionNumber,
                version.ActionType,
                version.CreatedAt,
                version.ActorUserId,
                version.ActorRole,
                IsRollbackEligible(version, club.CurrentVersionNumber),
                GetRollbackExpiry(version.CreatedAt),
                version.RollbackSourceVersionNumber,
                DeserializeChangedFields(version.ChangedFieldsJson),
                DeserializeSnapshot(version.SnapshotJson));
        }

        public async Task<ClubRollbackResult> RollbackToVersionAsync(
            int clubId,
            int versionNumber,
            int userId,
            string userRole)
        {
            var club = await GetTrackedClubOrThrowAsync(clubId);
            EnsureOwnerOrAdmin(club, userId, userRole);

            var targetVersion = await _db.ClubVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.ClubId == clubId && v.VersionNumber == versionNumber)
                ?? throw new ResourceNotFoundException(
                    $"Version {versionNumber} for club {clubId} was not found.");

            if (versionNumber == club.CurrentVersionNumber)
                throw new BadRequestException("Cannot roll back to the current version.");

            if (!IsRollbackEligible(targetVersion, club.CurrentVersionNumber))
                throw new BadRequestException("This version is no longer eligible for rollback.");

            var currentSnapshot = BuildSnapshot(club);
            var targetSnapshot = DeserializeSnapshot(targetVersion.SnapshotJson);

            ApplySnapshot(club, targetSnapshot);
            club.CurrentVersionNumber += 1;
            club.UpdatedAt = GetUtcNow();

            var changedFields = BuildChangedFields(currentSnapshot, BuildSnapshot(club));

            await using var transaction = await _db.Database.BeginTransactionAsync();

            await _db.SaveChangesAsync();

            AddVersionRecord(
                club,
                ClubVersionActions.Rollback,
                actorUserId: userId,
                actorRole: NormalizeActorRole(userRole),
                rollbackSourceVersionNumber: targetVersion.VersionNumber,
                changedFields: changedFields,
                createdAt: club.UpdatedAt);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await CacheClubAsync(club);
            await BumpClubListVersionAsync();

            return new ClubRollbackResult(club, targetVersion.VersionNumber, club.CurrentVersionNumber);
        }

        public Task EventCreatedAsync(int clubId, int eventId)
        {
            throw new shared.exceptions.http.NotImplementedException();
        }

        public Task EventDeletedAsync(int clubId, int eventId)
        {
            throw new shared.exceptions.http.NotImplementedException();
        }

        private async Task CacheClubAsync(Club club)
        {
            var payload = JsonSerializer.Serialize(ClubCacheMapper.ToDto(club));
            var expiry = WithJitter(ClubTTL);

            await _cache.SetValueAsync(GetClubCacheKey(club.Id), payload, expiry);
            await _cache.SetValueAsync(GetClubUserCacheKey(club.UserId), payload, expiry);
        }

        private async Task<long> GetClubListVersionAsync()
        {
            var v = await _cache.GetValueAsync(ClubListVersionKey);

            if (v == null)
            {
                await _cache.SetValueAsync(ClubListVersionKey, "1");
                return 1;
            }

            return long.Parse(v);
        }

        private async Task BumpClubListVersionAsync()
        {
            await _cache.IncrementAsync(ClubListVersionKey);
        }

        private static TimeSpan WithJitter(TimeSpan baseTtl, int percent = 20)
        {
            var delta = Random.Shared.Next(-percent, percent + 1);
            return baseTtl + TimeSpan.FromMilliseconds(
                baseTtl.TotalMilliseconds * delta / 100.0
            );
        }

        private async Task TrackClubAccessAsync(int clubId)
        {
            var counterKey = $"club:hot:count:{clubId}";
            var hotFlagKey = $"club:hot:{clubId}";

            if (await _cache.KeyExistsAsync(hotFlagKey))
                return;

            var count = await _cache.IncrementAsync(counterKey);

            if (count == 1)
            {
                await _cache.SetExpiryAsync(counterKey, HotWindow);
            }

            if (count >= HotThreshold)
            {
                await _cache.SetValueAsync(
                    hotFlagKey,
                    "1",
                    HotCooldown
                );

                Console.WriteLine("hot");
            }
        }

        private async Task<Club> GetTrackedClubOrThrowAsync(int clubId)
        {
            return await _db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId)
                ?? throw new ResourceNotFoundException($"Club {clubId} not found");
        }

        private async Task<Club> GetClubRecordOrThrowAsync(int clubId)
        {
            return await _db.Clubs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clubId)
                ?? throw new ResourceNotFoundException($"Club {clubId} not found");
        }

        private void AddVersionRecord(
            Club club,
            string actionType,
            int actorUserId,
            string actorRole,
            int? rollbackSourceVersionNumber,
            IReadOnlyList<ClubVersionFieldChange> changedFields,
            DateTime createdAt)
        {
            var snapshot = BuildSnapshot(club);

            _db.ClubVersions.Add(new ClubVersion
            {
                ClubId = club.Id,
                VersionNumber = club.CurrentVersionNumber,
                ActionType = actionType,
                SnapshotJson = JsonSerializer.Serialize(snapshot),
                ChangedFieldsJson = JsonSerializer.Serialize(changedFields),
                ClubImage = snapshot.ClubImage,
                ActorUserId = actorUserId,
                ActorRole = actorRole,
                RollbackSourceVersionNumber = rollbackSourceVersionNumber,
                CreatedAt = createdAt,
            });
        }

        private static ClubVersionSnapshot BuildSnapshot(Club club) => new()
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
            IsPrivate = club.isPrivate,
        };

        private static void ApplySnapshot(Club club, ClubVersionSnapshot snapshot)
        {
            club.Name = snapshot.Name;
            club.Description = snapshot.Description;
            club.Clubtype = ParseClubType(snapshot.Clubtype);
            club.ClubImage = snapshot.ClubImage;
            club.Phone = snapshot.Phone;
            club.Email = snapshot.Email;
            club.WebsiteUrl = snapshot.WebsiteUrl;
            club.Location = snapshot.Location;
            club.MaxMemberCount = snapshot.MaxMemberCount;
            club.isPrivate = snapshot.IsPrivate;
        }

        private static List<ClubVersionFieldChange> BuildChangedFields(
            ClubVersionSnapshot? previous,
            ClubVersionSnapshot current)
        {
            var changes = new List<ClubVersionFieldChange>();

            AddChange(changes, "name", previous?.Name, current.Name);
            AddChange(changes, "description", previous?.Description, current.Description);
            AddChange(changes, "clubtype", previous?.Clubtype, current.Clubtype);
            AddChange(changes, "clubImage", previous?.ClubImage, current.ClubImage);
            AddChange(changes, "phone", previous?.Phone, current.Phone);
            AddChange(changes, "email", previous?.Email, current.Email);
            AddChange(changes, "websiteUrl", previous?.WebsiteUrl, current.WebsiteUrl);
            AddChange(changes, "location", previous?.Location, current.Location);
            AddChange(changes, "maxMemberCount", previous?.MaxMemberCount, current.MaxMemberCount);
            AddChange(changes, "isPrivate", previous?.IsPrivate, current.IsPrivate);

            return changes;
        }

        private static void AddChange(
            ICollection<ClubVersionFieldChange> changes,
            string field,
            string? oldValue,
            string? newValue)
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

        private static void AddChange(
            ICollection<ClubVersionFieldChange> changes,
            string field,
            int? oldValue,
            int newValue)
        {
            if (oldValue.HasValue && oldValue.Value == newValue)
                return;

            changes.Add(new ClubVersionFieldChange
            {
                Field = field,
                OldValue = oldValue?.ToString(),
                NewValue = newValue.ToString()
            });
        }

        private static void AddChange(
            ICollection<ClubVersionFieldChange> changes,
            string field,
            bool? oldValue,
            bool newValue)
        {
            if (oldValue.HasValue && oldValue.Value == newValue)
                return;

            changes.Add(new ClubVersionFieldChange
            {
                Field = field,
                OldValue = oldValue?.ToString().ToLowerInvariant(),
                NewValue = newValue.ToString().ToLowerInvariant()
            });
        }

        private ClubVersionHistoryItem MapHistoryItem(ClubVersion version, int currentVersionNumber) => new(
            version.ClubId,
            version.VersionNumber,
            version.ActionType,
            version.CreatedAt,
            version.ActorUserId,
            version.ActorRole,
            IsRollbackEligible(version, currentVersionNumber),
            GetRollbackExpiry(version.CreatedAt),
            version.RollbackSourceVersionNumber,
            DeserializeChangedFields(version.ChangedFieldsJson));

        private bool IsRollbackEligible(ClubVersion version, int currentVersionNumber)
        {
            if (version.VersionNumber == currentVersionNumber)
                return false;

            return GetRollbackExpiry(version.CreatedAt) >= GetUtcNow();
        }

        private DateTime GetRollbackExpiry(DateTime createdAt) =>
            createdAt.AddDays(_versioningOptions.RollbackWindowDays);

        private DateTime GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

        private static ClubVersionSnapshot DeserializeSnapshot(string snapshotJson) =>
            JsonSerializer.Deserialize<ClubVersionSnapshot>(snapshotJson)
            ?? throw new InvalidOperationException("Club version snapshot could not be deserialized.");

        private static IReadOnlyList<ClubVersionFieldChange> DeserializeChangedFields(string changedFieldsJson) =>
            JsonSerializer.Deserialize<List<ClubVersionFieldChange>>(changedFieldsJson) ?? [];

        private static string GetClubCacheKey(int clubId) => $"club:{clubId}";

        private static string GetClubUserCacheKey(int userId) => $"club:user:{userId}";

        private static void EnsureOwnerOrAdmin(Club club, int userId, string userRole)
        {
            if (club.UserId == userId || IsAdminRole(userRole))
                return;

            throw new ForbiddenException("Not allowed");
        }

        private static bool IsAdminRole(string userRole) =>
            string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeActorRole(string actorRole) =>
            string.IsNullOrWhiteSpace(actorRole) ? "Unknown" : actorRole.Trim();

        private static int NormalizePage(int page) => page < 1 ? 1 : page;

        private static int NormalizePageSize(int pageSize) => pageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => pageSize
        };

        private static ClubType ParseClubType(string clubtype) =>
            clubtype.Trim().ToLowerInvariant() switch
            {
                "sport" or "sports" => ClubType.Sports,
                "academic" => ClubType.Academic,
                "social" => ClubType.Social,
                "cultural" or "music" => ClubType.Cultural,
                "game" or "gaming" => ClubType.Gaming,
                "other" => ClubType.Other,
                _ => Enum.Parse<ClubType>(clubtype, true)
            };
    }
}
