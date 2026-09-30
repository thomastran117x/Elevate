using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;


namespace backend.main.features.profile
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthUserRepository _authUserRepository;
        private readonly IAzureBlobService _blobService;
        private readonly IImageProcessor _imageProcessor;
        private readonly IFollowService _followService;
        private readonly ITokenService _tokenService;
        private readonly IRefreshAheadCache _refreshCache;
        private readonly IUsernameAvailabilityService _usernameAvailability;
        private readonly TimeProvider _timeProvider;
        private readonly ProfileOptions _profileOptions;

        private static readonly TimeSpan UserTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);

        private static string GetUserCacheKey(int userId) => $"user:{userId}";

        public UserService(
            IUserRepository userRepository,
            IAuthUserRepository authUserRepository,
            IAzureBlobService blobService,
            IImageProcessor imageProcessor,
            IFollowService followService,
            ITokenService tokenService,
            IRefreshAheadCache refreshCache,
            IUsernameAvailabilityService usernameAvailability,
            TimeProvider timeProvider,
            IOptions<ProfileOptions> profileOptions
        )
        {
            _userRepository = userRepository;
            _authUserRepository = authUserRepository;
            _blobService = blobService;
            _imageProcessor = imageProcessor;
            _followService = followService;
            _tokenService = tokenService;
            _refreshCache = refreshCache;
            _usernameAvailability = usernameAvailability;
            _timeProvider = timeProvider;
            _profileOptions = profileOptions.Value;
        }

        public async Task<IReadOnlyList<UserListRecord>> GetAllUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        )
        {
            return await _userRepository.GetUsersAsync(role, detail);
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            var user = await _refreshCache.GetOrSetAsync(
                GetUserCacheKey(id),
                () => _userRepository.GetUserAsync(id),
                UserTTL,
                nullSentinelTtl: NotFoundTTL);

            if (user == null)
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            return user;
        }

        public async Task<UserProfileRecord> GetPublicProfileByUsernameAsync(string username)
        {
            // Lookup, so Normalize rather than NormalizeAndValidate: usernames created before the
            // format rules existed still have to resolve their public profile. A value that no
            // longer satisfies the rules simply misses and becomes a 404 below.
            var normalizedUsername = UsernamePolicy.Normalize(username);
            var profile = await _userRepository.GetPublicProfileByUsernameOrReservationAsync(
                normalizedUsername,
                _timeProvider.GetUtcNow().UtcDateTime);
            if (profile == null)
                throw new ResourceNotFoundException(
                    $"No user found with the username {normalizedUsername}");

            return profile;
        }

        public async Task<User?> UpdateUserAsync(int id, User updatedUser)
        {
            var existingUser = await _userRepository.UpdatePartialAsync(updatedUser);
            if (existingUser == null)
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            await _refreshCache.RemoveAsync(GetUserCacheKey(id));
            return existingUser;
        }

        public async Task<User> ChangeUsernameAsync(int id, string username)
        {
            var forms = UsernamePolicy.NormalizeAndValidateWithDisplay(username);
            var normalizedUsername = forms.Username;
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var reservedUntilUtc = utcNow.AddDays(_profileOptions.UsernameChangeCooldownDays);

            var result = await _userRepository.ChangeUsernameAsync(
                id,
                normalizedUsername,
                forms.Display,
                utcNow,
                reservedUntilUtc);

            switch (result.Status)
            {
                case UsernameChangeStatus.Changed when result.User != null:
                    await _refreshCache.RemoveAsync(GetUserCacheKey(id));

                    // Both names are now occupied: the new one by the user, the old one by the
                    // reservation the change created. Recording only the new name would let the
                    // filter report the cooling-down name as free.
                    await _usernameAvailability.MarkTakenAsync(normalizedUsername);
                    if (!string.IsNullOrEmpty(result.PreviousUsername))
                        await _usernameAvailability.MarkTakenAsync(result.PreviousUsername);

                    return result.User;
                case UsernameChangeStatus.UserNotFound:
                    throw new ResourceNotFoundException($"User with the id {id} is not found");
                case UsernameChangeStatus.Unchanged:
                    // Reached only when the key and the casing both already match. A casing-only
                    // edit returns Changed, so this stays a genuine no-op rather than a rejection
                    // of ThomasT for an account currently holding thomast.
                    throw new BadRequestException("New username must be different from the current username.");
                case UsernameChangeStatus.CooldownActive when result.AvailableAtUtc is DateTime availableAtUtc:
                    throw new UsernameChangeCooldownException(availableAtUtc);
                case UsernameChangeStatus.Unavailable:
                    throw new UsernameTakenException(normalizedUsername);
                default:
                    throw new InternalServerErrorException();
            }
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            IReadOnlyList<string> orphanedBlobs = await _userRepository.DeleteUserAsync(id);

            // Best-effort cleanup of the avatar plus any cascade-deleted club/event images
            // (no-op for external/legacy URLs). Failures are swallowed inside DeleteBlobAsync.
            foreach (string blobUrl in orphanedBlobs)
                await _blobService.DeleteBlobAsync(blobUrl);

            await _refreshCache.RemoveAsync(GetUserCacheKey(id));
            return true;
        }

        public async Task<UserStatusRecord> UpdateUserStatusAsync(int id, bool isDisabled, string? reason)
        {
            var user = await _authUserRepository.UpdateUserStatusAsync(id, isDisabled, reason);
            if (user == null)
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            await _tokenService.RevokeAllRefreshSessionsAsync(id);
            await _refreshCache.RemoveAsync(GetUserCacheKey(id));
            return user;
        }

        public async Task<User> UpdateAvatarAsync(
            int id,
            IFormFile image,
            CancellationToken cancellationToken = default)
        {
            // Cheap existence check before any of the expensive work. A token outliving its account
            // would otherwise take one of the few processing slots, decode a full-size image and
            // write a blob, all to be told 404 at the end. Deliberately not kept: the write below
            // reads the row again, because anything read here is stale by the time decoding ends.
            if (!await _userRepository.ExistsAsync(id, cancellationToken))
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            // Decode and re-encode before touching storage: the stored avatar is WebP pixels only,
            // with no EXIF (GPS included) and nothing hidden past the image header. The token
            // matters: processing slots are process-wide, so an abandoned upload has to give its
            // slot back rather than finish decoding for a client that has gone.
            ProcessedImage processed;
            await using (var source = image.OpenReadStream())
            {
                processed = await _imageProcessor.ProcessAsync(source, cancellationToken);
            }

            // Deliberately not cancellable. Once the image is processed, the upload is the commit
            // point: a cancelled Put Blob may still have landed in storage, and with no URL back
            // there would be nothing to delete. Letting this small WebP write finish means every
            // blob it creates is either persisted below or removed by the catch.
            string filePath = await _blobService.UploadProcessedImageAsync(
                processed,
                "users",
                CancellationToken.None);

            AvatarSwapRecord swap;
            try
            {
                // Writes the avatar column only, and reports the URL it replaced as read in that
                // same unit of work. Sending the whole User back would revert any name, address or
                // phone change saved while the image was being processed.
                swap = await _userRepository.SwapAvatarAsync(id, filePath)
                    ?? throw new ResourceNotFoundException($"User with the id {id} is not found");
            }
            catch (AvatarSwapSupersededException superseded)
            {
                // The swap wrote, could not confirm it, and found another upload in charge. This
                // upload is unreferenced, and so is the URL that write replaced — nothing else will
                // ever report it, and the sweeper that would have caught it is opt-in.
                await _blobService.DeleteBlobAsync(filePath);

                if (!string.IsNullOrEmpty(superseded.ReplacedAvatarUrl) &&
                    superseded.ReplacedAvatarUrl != filePath)
                {
                    await _blobService.DeleteBlobAsync(superseded.ReplacedAvatarUrl);
                }

                throw;
            }
            catch (Exception exception) when (exception is ResourceNotFoundException or ConflictException)
            {
                // These two say the swap wrote nothing: the account is gone, or every attempt lost
                // its race. The upload is unreferenced for certain, so delete it.
                await _blobService.DeleteBlobAsync(filePath);
                throw;
            }
            catch (Exception exception)
            {
                // Anything else — a dropped connection, a retry limit — leaves it unknown whether
                // the swap committed. Deleting here would break the avatar of an account that now
                // points at this blob, so leave it to OrphanBlobCleanupRunner, which deletes only
                // blobs no row references. That sweeper is opt-in, so deployments that process
                // avatars should enable it; the alternative is risking a live avatar.
                // Without the exception: the controller's catch-all logs that, and two entries
                // per failure double the alert noise on a path this treats as expected.
                Logger.Warn(
                    $"[UserService] Avatar swap for user {id} failed after upload; leaving {filePath} for orphan cleanup.");

                // The swap may have committed, so anything cached for this user may now be stale.
                // Evicting is safe either way, and a cache fault must not replace the real error.
                try
                {
                    await _refreshCache.RemoveAsync(GetUserCacheKey(id));
                }
                catch (Exception cacheException)
                {
                    Logger.Warn(cacheException, $"[UserService] Cache eviction for user {id} failed.");
                }

                throw;
            }

            var updatedUser = swap.User;
            var previousAvatar = swap.PreviousAvatar;

            // Best-effort cleanup of the replaced image (no-op for external/legacy URLs). Racing
            // uploads each replace a different predecessor, so neither leaves the other's blob
            // behind.
            if (!string.IsNullOrEmpty(previousAvatar) && previousAvatar != filePath)
                await _blobService.DeleteBlobAsync(previousAvatar);

            await _refreshCache.RemoveAsync(GetUserCacheKey(id));
            return updatedUser;
        }

        public async Task<IEnumerable<FollowClub>> GetUserFollowingsAsync(int id, int page = 1, int pageSize = 20)
        {
            return await _followService.GetFollowsByUserAsync(id, page, pageSize);
        }
    }
}
