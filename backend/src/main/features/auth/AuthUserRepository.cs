using System.Data;

using backend.main.application.security;
using backend.main.features.auth.contracts;
using backend.main.features.media;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace backend.main.features.auth
{
    public class AuthUserRepository : IAuthUserRepository, IUserRepository
    {
        private readonly AppDatabaseContext _context;

        /// <summary>
        /// How many times an avatar swap re-reads and retries when another request replaced the
        /// avatar first. Each retry means a concurrent upload for the same account.
        /// </summary>
        private const int AvatarSwapAttempts = 3;

        /// <summary>What one attempt of <see cref="SwapAvatarAsync"/> settled on.</summary>
        private enum AvatarSwapKind
        {
            /// <summary>The account no longer exists.</summary>
            Missing,

            /// <summary>Another request replaced the avatar first; the attempt wrote nothing.</summary>
            Contended,

            /// <summary>The avatar column now holds this call's URL.</summary>
            Swapped,

            /// <summary>
            /// A re-run of an attempt that had already written found the column moved on, so
            /// another request owns the avatar and writing again could resurrect a deleted blob.
            /// </summary>
            Superseded
        }

        private readonly record struct AvatarSwapAttempt(
            AvatarSwapKind Kind,
            AvatarSwapRecord? Record,
            string? ReplacedAvatar = null)
        {
            public static AvatarSwapAttempt Missing => new(AvatarSwapKind.Missing, null);

            public static AvatarSwapAttempt Contended => new(AvatarSwapKind.Contended, null);

            public static AvatarSwapAttempt Swapped(AvatarSwapRecord record) =>
                new(AvatarSwapKind.Swapped, record);

            /// <param name="replacedAvatar">
            /// What the attempt read before writing. Carried on the record so the caller can clean
            /// it up; the user is never loaded on this path.
            /// </param>
            public static AvatarSwapAttempt Superseded(string? replacedAvatar) =>
                new(AvatarSwapKind.Superseded, null, replacedAvatar);
        }

        public AuthUserRepository(AppDatabaseContext context) => _context = context;

        public async Task<User> CreateUserAsync(User user)
        {
            user.Usertype = AuthRoles.NormalizeStored(user.Usertype);
            if (!string.IsNullOrWhiteSpace(user.Username))
            {
                var forms = UsernamePolicy.NormalizeAndValidateWithDisplay(user.Username);
                user.Username = forms.Username;

                // The choke point for the display invariant. Every account creation passes through
                // here, so re-deriving the display when the caller did not set one — or set one that
                // does not normalise back to the username — makes Normalize(display) == Username
                // hold regardless of which caller is wrong. It also covers the rollout: a signup
                // that was stashed in a verification token before this column existed deserialises
                // with a null display and is repaired here rather than stored inconsistent.
                if (!UsernamePolicy.IsValidDisplayFor(forms.Username, user.UsernameDisplay))
                    user.UsernameDisplay = forms.Display;
            }
            else
            {
                // No username means no display. Leaving a stale one would be a row whose only
                // rendered handle belongs to a name the account does not hold.
                user.UsernameDisplay = null;
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                try
                {
                    if (user.Username != null)
                    {
                        var reservation = await _context.UsernameReservations
                            .FindAsync(user.Username);
                        if (reservation?.ReservedUntilUtc > DateTime.UtcNow)
                            throw new UsernameTakenException(user.Username);

                        if (reservation != null)
                            _context.UsernameReservations.Remove(reservation);
                    }

                    await _context.Users.AddAsync(user);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return user;
                }
                // The availability check callers run before this happens outside the transaction,
                // so two signups can both see the same name as free and race to insert it. The
                // loser hits the unique index; without this it would surface as a 500 rather than
                // the 409 the caller already knows how to turn into "pick another name".
                catch (Exception exception)
                    when (user.Username != null && IsUsernameUniqueViolation(exception))
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    throw new UsernameTakenException(user.Username);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<User?> UpdateUserAsync(int id, User updated)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            user.Password = updated.Password ?? user.Password;
            user.Usertype = updated.Usertype != null
                ? AuthRoles.NormalizeStored(updated.Usertype)
                : user.Usertype;
            user.Name = updated.Name ?? user.Name;
            user.Avatar = updated.Avatar ?? user.Avatar;
            user.Address = updated.Address ?? user.Address;
            user.Phone = updated.Phone ?? user.Phone;

            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<User?> UpdatePartialAsync(User updated)
        {
            var existing = await _context.Users.FindAsync(updated.Id);
            if (existing == null)
                return null;

            // Identity and role are intentionally NOT mutable through a partial update.
            // Email changes require re-verification and role changes go through dedicated
            // admin/status flows; otherwise a stale JWT claim could silently overwrite them.
            if (updated.Name != null)
                existing.Name = updated.Name;
            if (updated.Avatar != null)
                existing.Avatar = updated.Avatar;
            if (updated.Address != null)
                existing.Address = updated.Address;
            if (updated.Phone != null)
                existing.Phone = updated.Phone;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) =>
            await _context.Users.AsNoTracking().AnyAsync(u => u.Id == id, cancellationToken);

        public async Task<AvatarSwapRecord?> SwapAvatarAsync(int id, string avatarUrl)
        {
            // Compare-and-swap, because two uploads for the same account run in separate scopes
            // with their own DbContext. A read followed by an unconditional write would let both
            // observe the same predecessor: last write wins, both callers delete that shared
            // predecessor, and the losing request's freshly uploaded blob is left with nothing
            // holding its URL. The update carries the observed value in its WHERE clause instead,
            // so exactly one of them matches and the other retries against the winner's value.
            var strategy = _context.Database.CreateExecutionStrategy();

            for (var attempt = 1; attempt <= AvatarSwapAttempts; attempt++)
            {
                // What this attempt's first run read, and whether it ever reached its commit.
                // Scoped to the attempt: the next one round the loop starts from whatever the
                // request that beat it wrote.
                string? observedAvatar = null;
                var commitAttempted = false;

                // The read, the update and the reload that reports the result commit together, and
                // verifySucceeded settles the one case the transaction cannot: a commit that lands
                // and then fails to report back. EF asks it whether the write is there, and on a
                // yes hands back this run's result — the real predecessor included — rather than
                // running the operation a second time against the state it just wrote.
                var outcome = await strategy.ExecuteInTransactionAsync(
                    operation: async cancellationToken =>
                    {
                        var current = await _context.Users
                            .AsNoTracking()
                            .Where(u => u.Id == id)
                            .Select(u => new { u.Avatar })
                            .FirstOrDefaultAsync(cancellationToken);

                        // No row: the account was deleted, most likely while its image was being
                        // processed.
                        if (current == null)
                            return AvatarSwapAttempt.Missing;

                        var previousAvatar = current.Avatar;

                        // A re-run of an attempt that got as far as committing, finding the
                        // column moved on. The commit either failed and another request got in, or
                        // it landed and that request has since replaced it — and in that second
                        // case the request has already deleted this call's blob. Writing the URL
                        // again would point the account at bytes that no longer exist, so give up
                        // and report what this attempt replaced, which is now referenced by
                        // nothing either way.
                        //
                        // Only once a commit was attempted. A run that failed before then — a
                        // matched-nothing update, or a fault on the reload below — rolled back
                        // with certainty, so it is free to try again against the new value.
                        if (commitAttempted && previousAvatar != observedAvatar)
                            return AvatarSwapAttempt.Superseded(observedAvatar);

                        observedAvatar = previousAvatar;

                        // A null previousAvatar is fine as a parameter: EF compiles the comparison
                        // to IS NULL when the value is null, and caches the two shapes separately.
                        var affected = await _context.Users
                            .Where(u => u.Id == id && u.Avatar == previousAvatar)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(u => u.Avatar, avatarUrl),
                                cancellationToken);

                        // Another request swapped the avatar between the read and the update. The
                        // transaction commits nothing, and the outer loop starts again from the
                        // value that request wrote.
                        if (affected == 0)
                            return AvatarSwapAttempt.Contended;

                        var updated = await GetUserAsync(id);
                        if (updated == null)
                            return AvatarSwapAttempt.Missing;

                        // Last thing before returning, because returning is what makes EF commit.
                        // Set any earlier and a fault in the reload above — which rolls back
                        // without a commit ever being tried — would look ambiguous on the re-run.
                        commitAttempted = true;
                        return AvatarSwapAttempt.Swapped(new AvatarSwapRecord(updated, previousAvatar));
                    },
                    verifySucceeded: async cancellationToken => await _context.Users
                        .AsNoTracking()
                        .AnyAsync(u => u.Id == id && u.Avatar == avatarUrl, cancellationToken),
                    cancellationToken: default);

                switch (outcome.Kind)
                {
                    case AvatarSwapKind.Missing:
                        return null;
                    case AvatarSwapKind.Swapped:
                        return outcome.Record;
                    case AvatarSwapKind.Superseded:
                        // Another request owns the avatar and this call cannot safely write again.
                        // Retrying would only race the same way, so stop here. The URL this
                        // attempt replaced goes with the conflict: if its write did commit, this
                        // is the only report of that URL anyone gets, and the account no longer
                        // points at it either way.
                        throw new AvatarSwapSupersededException(outcome.ReplacedAvatar);
                    case AvatarSwapKind.Contended:
                    default:
                        continue;
                }
            }

            // Every attempt lost its race, which needs a burst of concurrent uploads for one
            // account. Nothing of this call was committed, so the caller can delete the blob it
            // uploaded without risk of removing one the account still points at.
            throw new ConflictException(AvatarSwapSupersededException.AvatarChangedMessage);
        }

        public async Task<UserOAuthRecord?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            if (googleId != null)
                user.GoogleID = googleId;
            if (microsoftId != null)
                user.MicrosoftID = microsoftId;

            await _context.SaveChangesAsync();
            return ToOAuthRecord(user);
        }

        public async Task<UserStatusRecord?> UpdateUserStatusAsync(int id, bool isDisabled, string? disabledReason)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return null;

            user.IsDisabled = isDisabled;
            user.DisabledAtUtc = isDisabled ? DateTime.UtcNow : null;
            user.DisabledReason = isDisabled ? disabledReason : null;
            user.AuthVersion += 1;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return new UserStatusRecord
            {
                Id = user.Id,
                IsDisabled = user.IsDisabled,
                DisabledAtUtc = user.DisabledAtUtc,
                DisabledReason = user.DisabledReason,
                AuthVersion = user.AuthVersion,
            };
        }

        public async Task<bool> IncrementAuthVersionAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return false;

            user.AuthVersion += 1;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IReadOnlyList<string>> DeleteUserAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return Array.Empty<string>();

            // The context enables retry-on-failure, and that strategy rejects a
            // user-initiated transaction unless the whole unit runs through it.
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                // Deleting the user cascades to their clubs, club versions, events and event
                // images (all DeleteBehavior.Cascade). EF only removes the rows, so the blob URLs
                // those rows carry become unrecoverable once the cascade runs. Gather them here,
                // before the delete, so the caller can clean up the orphaned blobs afterwards.
                var ownedClubIds = await _context.Clubs
                    .Where(club => club.UserId == id)
                    .Select(club => club.Id)
                    .ToListAsync();

                var orphanedBlobUrls = new List<string>();
                if (!string.IsNullOrEmpty(user.Avatar))
                    orphanedBlobUrls.Add(user.Avatar);

                if (ownedClubIds.Count > 0)
                {
                    orphanedBlobUrls.AddRange(await _context.Clubs
                        .Where(club => ownedClubIds.Contains(club.Id)
                            && club.ClubImage != null && club.ClubImage != string.Empty)
                        .Select(club => club.ClubImage!)
                        .ToListAsync());

                    orphanedBlobUrls.AddRange(await _context.Clubs
                        .Where(club => ownedClubIds.Contains(club.Id)
                            && club.BannerImage != null && club.BannerImage != string.Empty)
                        .Select(club => club.BannerImage!)
                        .ToListAsync());

                    // A JSON column behind a value converter: materialize the lists and flatten
                    // them here, since there is nothing in SQL to select the URLs out of.
                    var galleries = await _context.Clubs
                        .Where(club => ownedClubIds.Contains(club.Id))
                        .Select(club => club.GalleryImages)
                        .ToListAsync();
                    orphanedBlobUrls.AddRange(galleries
                        .SelectMany(gallery => gallery ?? [])
                        .Where(url => !string.IsNullOrEmpty(url)));

                    orphanedBlobUrls.AddRange(await _context.ClubVersions
                        .Where(version => ownedClubIds.Contains(version.ClubId)
                            && version.ClubImage != null && version.ClubImage != string.Empty)
                        .Select(version => version.ClubImage!)
                        .ToListAsync());

                    var ownedEventIds = await _context.Events
                        .Where(ev => ownedClubIds.Contains(ev.ClubId))
                        .Select(ev => ev.Id)
                        .ToListAsync();

                    if (ownedEventIds.Count > 0)
                    {
                        orphanedBlobUrls.AddRange(await _context.EventImages
                            .Where(image => ownedEventIds.Contains(image.EventId)
                                && image.ImageUrl != null && image.ImageUrl != string.Empty)
                            .Select(image => image.ImageUrl!)
                            .ToListAsync());
                    }
                }

                // ClubStaff.GrantedByUserId is a Restrict FK, so staff roles this user granted to
                // others would block the delete. Reassign those grants to the club's owner (falling
                // back to the affected member) so the role survives and the account can be removed.
                var grantsByUser = await _context.ClubStaff
                    .Where(staff => staff.GrantedByUserId == id)
                    .ToListAsync();

                if (grantsByUser.Count > 0)
                {
                    var grantClubIds = grantsByUser.Select(staff => staff.ClubId).Distinct().ToList();
                    var clubOwners = await _context.Clubs
                        .Where(club => grantClubIds.Contains(club.Id))
                        .ToDictionaryAsync(club => club.Id, club => club.UserId);

                    foreach (var grant in grantsByUser)
                    {
                        var ownerId = clubOwners.TryGetValue(grant.ClubId, out var owner)
                            ? owner
                            : grant.UserId;
                        grant.GrantedByUserId = ownerId != id ? ownerId : grant.UserId;
                    }

                    await _context.SaveChangesAsync();
                }

                // Media assets are an upload ledger with no cascade from the rows above. Those
                // whose image is going away with this account go too, so no Ready row is left
                // advertising a URL with nothing behind it. So do the uploader's unfinished
                // uploads; the quarantine reaper removes their bytes. Ready images the user put in other people's clubs survive,
                // with the owner set to null by the foreign key.
                var deletedUrls = orphanedBlobUrls.Distinct(StringComparer.Ordinal).ToList();
                await _context.MediaAssets
                    .Where(asset =>
                        (asset.PublicUrl != null && deletedUrls.Contains(asset.PublicUrl)) ||
                        (asset.OwnerUserId == id && asset.Status != MediaAssetStatus.Ready))
                    .ExecuteDeleteAsync();

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return orphanedBlobUrls
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            });
        }

        public async Task<User?> GetUserAsync(int id)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new User
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = null,
                    HasLocalPassword = u.Password != null,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    Name = u.Name,
                    Username = u.Username,
                    UsernameDisplay = u.UsernameDisplay,
                    UsernameChangeAvailableAtUtc = u.UsernameChangeAvailableAtUtc,
                    Avatar = u.Avatar,
                    Address = u.Address,
                    Phone = u.Phone,
                    MicrosoftID = u.MicrosoftID,
                    GoogleID = u.GoogleID,
                    IsDisabled = u.IsDisabled,
                    DisabledAtUtc = u.DisabledAtUtc,
                    DisabledReason = u.DisabledReason,
                    AuthVersion = u.AuthVersion,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserAuthRecord?> GetAuthByUsernameAsync(string username)
        {
            return await GetAuthRecords()
                .Where(u => u.Username == username)
                .Select(u => new UserAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = u.Password,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    Name = u.Name,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserAuthRecord?> GetAuthByEmailAsync(string email)
        {
            return await GetAuthRecords()
                .Where(u => u.Email == email)
                .Select(u => new UserAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = u.Password,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    Name = u.Name,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserAuthRecord?> GetAuthByIdAsync(int id)
        {
            return await GetAuthRecords()
                .Where(u => u.Id == id)
                .Select(u => new UserAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Password = u.Password,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    Name = u.Name,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public Task<UserRecoveryRecord?> GetRecoveryByUsernameAsync(string username) =>
            GetRecoveryRecords()
                .Where(u => u.Username == username)
                .FirstOrDefaultAsync();

        public Task<UserRecoveryRecord?> GetRecoveryByEmailAsync(string email) =>
            GetRecoveryRecords()
                .Where(u => u.Email == email)
                .FirstOrDefaultAsync();

        private IQueryable<UserRecoveryRecord> GetRecoveryRecords() =>
            _context.Users
                .AsNoTracking()
                .Select(u => new UserRecoveryRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = u.Username,
                    RecipientName = u.Name,
                    IsDisabled = u.IsDisabled,
                    HasLocalPassword = u.Password != null,
                    HasGoogleProvider = u.GoogleID != null,
                    HasMicrosoftProvider = u.MicrosoftID != null,
                });

        private IQueryable<User> GetAuthRecords() => _context.Users.AsNoTracking();

        public async Task<UserOAuthRecord?> GetOAuthByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserOAuthRecord?> GetOAuthByMicrosoftIdAsync(string microsoftId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.MicrosoftID == microsoftId)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserOAuthRecord?> GetOAuthByGoogleIdAsync(string googleId)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.GoogleID == googleId)
                .Select(u => new UserOAuthRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    GoogleID = u.GoogleID,
                    MicrosoftID = u.MicrosoftID,
                    IsDisabled = u.IsDisabled,
                    AuthVersion = u.AuthVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserProfileRecord?> GetProfileByUsernameAsync(string username)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Username == username)
                .Select(u => new UserProfileRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                    UsernameDisplay = string.IsNullOrWhiteSpace(u.Username)
                        ? u.Email
                        : (u.UsernameDisplay ?? u.Username),
                    Name = u.Name,
                    Avatar = u.Avatar,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    CreatedAtUtc = u.CreatedAt,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserProfileRecord?> GetPublicProfileByUsernameOrReservationAsync(
            string username,
            DateTime utcNow)
        {
            var direct = await GetProfileByUsernameAsync(username);
            if (direct != null)
                return direct;

            var userId = await _context.UsernameReservations
                .AsNoTracking()
                .Where(reservation =>
                    reservation.Username == username
                    && reservation.ReservedUntilUtc > utcNow)
                .Select(reservation => (int?)reservation.UserId)
                .FirstOrDefaultAsync();

            if (userId == null)
                return null;

            return await _context.Users
                .AsNoTracking()
                .Where(user => user.Id == userId.Value)
                .Select(user => new UserProfileRecord
                {
                    Id = user.Id,
                    Email = user.Email,
                    Username = string.IsNullOrWhiteSpace(user.Username) ? user.Email : user.Username,
                    UsernameDisplay = string.IsNullOrWhiteSpace(user.Username)
                        ? user.Email
                        : (user.UsernameDisplay ?? user.Username),
                    Name = user.Name,
                    Avatar = user.Avatar,
                    Usertype = AuthRoles.NormalizeStored(user.Usertype),
                    CreatedAtUtc = user.CreatedAt,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<UserProfileRecord?> GetProfileByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Email == email)
                .Select(u => new UserProfileRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                    UsernameDisplay = string.IsNullOrWhiteSpace(u.Username)
                        ? u.Email
                        : (u.UsernameDisplay ?? u.Username),
                    Name = u.Name,
                    Avatar = u.Avatar,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    CreatedAtUtc = u.CreatedAt,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<IReadOnlyList<UserListRecord>> GetUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        )
        {
            var query = _context.Users
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrEmpty(role))
            {
                var normalizedRole = AuthRoles.NormalizeStored(role);
                query = query.Where(u => u.Usertype == normalizedRole);
            }

            return await ProjectUserListQuery(query, detail).ToListAsync();
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == email);
        }

        public async Task<bool> UsernameExistsAsync(string username, int excludeUserId)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Username == username && u.Id != excludeUserId);
        }

        public async Task<bool> UsernameUnavailableAsync(string username, DateTime utcNow)
        {
            if (await _context.Users.AsNoTracking().AnyAsync(user => user.Username == username))
                return true;

            return await _context.UsernameReservations
                .AsNoTracking()
                .AnyAsync(reservation =>
                    reservation.Username == username
                    && reservation.ReservedUntilUtc > utcNow);
        }

        public async Task<IReadOnlySet<string>> FindUnavailableUsernamesAsync(
            IReadOnlyCollection<string> usernames,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            if (usernames.Count == 0)
                return new HashSet<string>(StringComparer.Ordinal);

            // Distinct so a repeated candidate cannot widen the IN list, and materialised once
            // because EF translates the same local collection into both halves of the union.
            var candidates = usernames.Distinct(StringComparer.Ordinal).ToList();

            // The same predicate as UsernameUnavailableAsync, evaluated over a set: a name is taken
            // if a user holds it or an unexpired reservation still covers it. Both halves have to be
            // asked, or a name cooling down after a rename would be offered as free.
            var held = _context.Users
                .AsNoTracking()
                .Where(user => user.Username != null && candidates.Contains(user.Username))
                .Select(user => user.Username!);

            var reserved = _context.UsernameReservations
                .AsNoTracking()
                .Where(reservation =>
                    candidates.Contains(reservation.Username)
                    && reservation.ReservedUntilUtc > utcNow)
                .Select(reservation => reservation.Username);

            // Composed into a single UNION rather than awaited one after the other. Awaiting each
            // half separately would be two round trips per call, and the generator can call this
            // once per draw — so the batching this method exists for would be half undone.
            var names = await held.Union(reserved).ToListAsync(cancellationToken);

            // Username is citext, so the database may return a different casing than was asked for.
            // Normalize on the way out so the caller can match on the string it passed in.
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var username in names)
                taken.Add(UsernamePolicy.Normalize(username));

            return taken;
        }

        public async Task<UsernameChangeRecord> ChangeUsernameAsync(
            int userId,
            string username,
            string usernameDisplay,
            DateTime utcNow,
            DateTime reservedUntilUtc)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                try
                {
                    // PostgreSQL's row lock serializes changes for the same account. The
                    // serializable transaction also protects the cross-table namespace check for
                    // usernames that do not yet have a row to lock.
                    var user = _context.Database.IsNpgsql()
                        ? await _context.Users
                            .FromSqlInterpolated(
                                $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                            .SingleOrDefaultAsync()
                        : await _context.Users.FindAsync(userId);
                    if (user == null)
                        return new UsernameChangeRecord(UsernameChangeStatus.UserNotFound);

                    // The name being replaced, so Normalize rather than NormalizeAndValidate: it
                    // may predate the format rules, and validating it here would leave the owner
                    // permanently unable to rename away from it.
                    var currentUsername = string.IsNullOrWhiteSpace(user.Username)
                        ? null
                        : UsernamePolicy.Normalize(user.Username);

                    if (currentUsername == username)
                    {
                        // Same key, different casing — thomast -> ThomasT. Nothing moved: no link
                        // breaks, no name is released for someone else to take, and no reservation
                        // is warranted. So this updates the display alone and deliberately leaves
                        // UsernameChangeAvailableAtUtc untouched; spending a 30-day cooldown on a
                        // capitalisation fix would strand the owner for a month over cosmetics.
                        if (UsernamePolicy.IsValidDisplayFor(username, user.UsernameDisplay)
                            && string.Equals(user.UsernameDisplay, usernameDisplay, StringComparison.Ordinal))
                        {
                            return new UsernameChangeRecord(UsernameChangeStatus.Unchanged, user);
                        }

                        user.UsernameDisplay = usernameDisplay;
                        user.UpdatedAt = utcNow;
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        return new UsernameChangeRecord(UsernameChangeStatus.Changed, user);
                    }

                    if (currentUsername != null
                        && user.UsernameChangeAvailableAtUtc is DateTime availableAtUtc
                        && availableAtUtc > utcNow)
                    {
                        return new UsernameChangeRecord(
                            UsernameChangeStatus.CooldownActive,
                            user,
                            availableAtUtc);
                    }

                    if (await _context.Users
                        .AsNoTracking()
                        .AnyAsync(other => other.Id != userId && other.Username == username))
                    {
                        return new UsernameChangeRecord(UsernameChangeStatus.Unavailable, user);
                    }

                    var requestedReservation = await _context.UsernameReservations
                        .FindAsync(username);
                    if (requestedReservation != null)
                    {
                        if (requestedReservation.ReservedUntilUtc > utcNow)
                            return new UsernameChangeRecord(UsernameChangeStatus.Unavailable, user);

                        _context.UsernameReservations.Remove(requestedReservation);
                    }

                    if (currentUsername != null)
                    {
                        var oldReservation = await _context.UsernameReservations
                            .FindAsync(currentUsername);
                        if (oldReservation == null)
                        {
                            _context.UsernameReservations.Add(new UsernameReservation
                            {
                                Username = currentUsername,
                                UserId = userId,
                                ReservedUntilUtc = reservedUntilUtc,
                            });
                        }
                        else
                        {
                            oldReservation.UserId = userId;
                            oldReservation.ReservedUntilUtc = reservedUntilUtc;
                        }
                    }

                    user.Username = username;
                    user.UsernameDisplay = usernameDisplay;
                    user.UsernameChangeAvailableAtUtc = currentUsername == null
                        ? null
                        : reservedUntilUtc;
                    user.UpdatedAt = utcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new UsernameChangeRecord(
                        UsernameChangeStatus.Changed,
                        user,
                        PreviousUsername: currentUsername);
                }
                catch (Exception exception) when (IsWriteConflict(exception))
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    return new UsernameChangeRecord(UsernameChangeStatus.Unavailable);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        public async Task<EmailChangeRecord> ChangeEmailAsync(
            int userId,
            string email,
            int expectedAuthVersion,
            DateTime utcNow)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
                try
                {
                    // Same row lock as ChangeUsernameAsync: it serializes concurrent changes to
                    // this account, while the serializable transaction covers the uniqueness check
                    // against an address that has no row of its own to lock.
                    var user = _context.Database.IsNpgsql()
                        ? await _context.Users
                            .FromSqlInterpolated(
                                $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                            .SingleOrDefaultAsync()
                        : await _context.Users.FindAsync(userId);
                    if (user == null)
                        return new EmailChangeRecord(EmailChangeStatus.UserNotFound);

                    // Verified here rather than by the caller: the row is locked, so this is the
                    // only place the version can be compared without a window in which a password
                    // change could commit between the check and the write.
                    if (user.AuthVersion != expectedAuthVersion)
                        return new EmailChangeRecord(EmailChangeStatus.Stale, user);

                    var previousEmail = user.Email;

                    // Email is a citext column, so the database compares case-insensitively and
                    // this normalised comparison matches what the unique index would enforce.
                    if (EmailPolicy.Normalize(previousEmail) == EmailPolicy.Normalize(email))
                        return new EmailChangeRecord(EmailChangeStatus.Unchanged, user);

                    if (await _context.Users
                        .AsNoTracking()
                        .AnyAsync(other => other.Id != userId && other.Email == email))
                    {
                        return new EmailChangeRecord(EmailChangeStatus.Unavailable, user);
                    }

                    user.Email = email;
                    // Bumped in the same SaveChanges as the address itself: the email is an access
                    // token claim, so a change that landed without invalidating outstanding tokens
                    // would leave live sessions authenticating as an address the account no longer
                    // owns. See JwtConfiguration.OnTokenValidated.
                    user.AuthVersion += 1;
                    user.UpdatedAt = utcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new EmailChangeRecord(
                        EmailChangeStatus.Changed,
                        user,
                        PreviousEmail: previousEmail);
                }
                catch (Exception exception) when (IsWriteConflict(exception))
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    return new EmailChangeRecord(EmailChangeStatus.Unavailable);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        /// <summary>Name EF gives the unique index on <c>Users.Username</c>.</summary>
        private const string UsernameUniqueIndexName = "IX_Users_Username";

        /// <summary>
        /// Whether a failed write lost the race on the unique username index specifically.
        /// </summary>
        /// <remarks>
        /// Deliberately narrower than <see cref="IsWriteConflict"/>, which answers "lost a race on
        /// something" for any constraint. <see cref="CreateUserAsync"/> can equally collide on
        /// Email, GoogleID or MicrosoftID, and reporting one of those as a taken username would
        /// send the caller off to change the wrong field. Postgres names the index it violated;
        /// SQLite, used by the repository tests, only names the columns in its message.
        /// </remarks>
        private static bool IsUsernameUniqueViolation(Exception exception)
        {
            var databaseException = exception is DbUpdateException
                ? exception.InnerException
                : exception;

            return databaseException switch
            {
                PostgresException postgres =>
                    postgres.SqlState == PostgresErrorCodes.UniqueViolation
                    && string.Equals(
                        postgres.ConstraintName,
                        UsernameUniqueIndexName,
                        StringComparison.Ordinal),
                SqliteException { SqliteErrorCode: 19 } sqlite =>
                    sqlite.Message.Contains("Users.Username", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static bool IsWriteConflict(Exception exception)
        {
            var databaseException = exception is DbUpdateException
                ? exception.InnerException
                : exception;

            return databaseException is PostgresException postgresException
                    && postgresException.SqlState is
                        PostgresErrorCodes.UniqueViolation
                        or PostgresErrorCodes.SerializationFailure
                        or PostgresErrorCodes.DeadlockDetected
                || databaseException is SqliteException { SqliteErrorCode: 19 };
        }

        public async Task<IReadOnlyList<UserListRecord>> GetByIdsAsync(
            IEnumerable<int> ids,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        )
        {
            var idList = ids.Distinct().ToList();

            if (idList.Count == 0)
                return [];

            var users = await ProjectUserListQuery(
                    _context.Users
                        .AsNoTracking()
                        .Where(u => idList.Contains(u.Id)),
                    detail
                )
                .ToListAsync();

            return idList
                .Select(id => users.FirstOrDefault(u => u.Id == id))
                .Where(user => user != null)
                .Cast<UserListRecord>()
                .ToList();
        }

        private static IQueryable<UserListRecord> ProjectUserListQuery(
            IQueryable<User> query,
            UserReadDetailLevel detail
        )
        {
            if (detail == UserReadDetailLevel.Admin)
            {
                return query.Select(u => new UserListRecord
                {
                    Id = u.Id,
                    Email = u.Email,
                    Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                    UsernameDisplay = string.IsNullOrWhiteSpace(u.Username)
                        ? u.Email
                        : (u.UsernameDisplay ?? u.Username),
                    Name = u.Name,
                    Avatar = u.Avatar,
                    Usertype = AuthRoles.NormalizeStored(u.Usertype),
                    IsDisabled = u.IsDisabled,
                    DisabledAtUtc = u.DisabledAtUtc,
                    DisabledReason = u.DisabledReason,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                });
            }

            return query.Select(u => new UserListRecord
            {
                Id = u.Id,
                Email = u.Email,
                Username = string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username,
                UsernameDisplay = string.IsNullOrWhiteSpace(u.Username)
                    ? u.Email
                    : (u.UsernameDisplay ?? u.Username),
                Name = u.Name,
                Avatar = u.Avatar,
                Usertype = AuthRoles.NormalizeStored(u.Usertype),
                IsDisabled = null,
                DisabledAtUtc = null,
                DisabledReason = null,
                CreatedAt = null,
                UpdatedAt = null,
            });
        }

        private static UserOAuthRecord ToOAuthRecord(User user)
        {
            return new UserOAuthRecord
            {
                Id = user.Id,
                Email = user.Email,
                Usertype = AuthRoles.NormalizeStored(user.Usertype),
                GoogleID = user.GoogleID,
                MicrosoftID = user.MicrosoftID,
                IsDisabled = user.IsDisabled,
                AuthVersion = user.AuthVersion,
            };
        }
    }
}
