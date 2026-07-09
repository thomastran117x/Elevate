using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;


namespace backend.main.features.profile
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthUserRepository _authUserRepository;
        private readonly IAzureBlobService _blobService;
        private readonly IFollowService _followService;
        private readonly ITokenService _tokenService;
        private readonly IRefreshAheadCache _refreshCache;

        private static readonly TimeSpan UserTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NotFoundTTL = TimeSpan.FromSeconds(15);

        private static string GetUserCacheKey(int userId) => $"user:{userId}";

        public UserService(
            IUserRepository userRepository,
            IAuthUserRepository authUserRepository,
            IAzureBlobService blobService,
            IFollowService followService,
            ITokenService tokenService,
            IRefreshAheadCache refreshCache
        )
        {
            _userRepository = userRepository;
            _authUserRepository = authUserRepository;
            _blobService = blobService;
            _followService = followService;
            _tokenService = tokenService;
            _refreshCache = refreshCache;
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
            var profile = await _userRepository.GetProfileByUsernameAsync(username);
            if (profile == null)
                throw new ResourceNotFoundException($"No user found with the username {username}");

            return profile;
        }

        public async Task<User?> UpdateUserAsync(int id, User updatedUser)
        {
            if (!string.IsNullOrWhiteSpace(updatedUser.Username)
                && await _userRepository.UsernameExistsAsync(updatedUser.Username, id))
            {
                throw new ConflictException($"The username '{updatedUser.Username}' is already taken.");
            }

            var existingUser = await _userRepository.UpdatePartialAsync(updatedUser);
            if (existingUser == null)
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            await _refreshCache.RemoveAsync(GetUserCacheKey(id));
            return existingUser;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            _ = await _userRepository.DeleteUserAsync(id);
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

        public async Task<User?> UpdateAvatarAsync(int id, IFormFile image)
        {
            // Verify the user exists before writing anything to blob storage, so a
            // deleted/missing account can't leave an orphaned upload behind.
            User user = await _userRepository.GetUserAsync(id)
                ?? throw new ResourceNotFoundException($"User with the id {id} is not found");

            string? previousAvatar = user.Avatar;
            string filePath = await _blobService.UploadImageAsync(image, "users");
            user.Avatar = filePath;

            User updatedUser = await _userRepository.UpdatePartialAsync(user)
                ?? throw new ResourceNotFoundException($"User with the id {id} is not found");

            // Best-effort cleanup of the replaced image (no-op for external/legacy URLs).
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
