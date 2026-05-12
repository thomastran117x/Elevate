using backend.main.shared.exceptions.http;
using backend.main.models.core;
using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.token;
using backend.main.features.clubs.follow;
using backend.main.features.profile.contracts;
using backend.main.shared.storage;


namespace backend.main.features.profile
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthUserRepository _authUserRepository;
        private readonly IFileUploadService _fileService;
        private readonly IFollowService _followService;
        private readonly ITokenService _tokenService;
        public UserService(
            IUserRepository userRepository,
            IAuthUserRepository authUserRepository,
            IFileUploadService fileService,
            IFollowService followService,
            ITokenService tokenService
        )
        {
            _userRepository = userRepository;
            _authUserRepository = authUserRepository;
            _fileService = fileService;
            _followService = followService;
            _tokenService = tokenService;
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
            var user = await _userRepository.GetUserAsync(id);
            if (user == null)
            {
                throw new ResourceNotFoundException($"User with the id {id} is not found");
            }
            return user;
        }

        public async Task<User?> UpdateUserAsync(int id, User updatedUser)
        {
            var existingUser = await _userRepository.UpdatePartialAsync(updatedUser);
            if (existingUser == null)
            {
                throw new ResourceNotFoundException($"User with the id {id} is not found");
            }

            return existingUser;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            _ = await _userRepository.DeleteUserAsync(id);
            return true;
        }

        public async Task<UserStatusRecord> UpdateUserStatusAsync(int id, bool isDisabled, string? reason)
        {
            var user = await _authUserRepository.UpdateUserStatusAsync(id, isDisabled, reason);
            if (user == null)
                throw new ResourceNotFoundException($"User with the id {id} is not found");

            await _tokenService.RevokeAllRefreshSessionsAsync(id);
            return user;
        }

        public async Task<User?> UpdateAvatarAsync(int id, IFormFile image)
        {
            string filePath = await _fileService.UploadImageAsync(image, "users");

            User user = await _userRepository.GetUserAsync(id)
                ?? throw new ResourceNotFoundException($"User with the id {id} is not found");

            user.Avatar = filePath;

            User updatedUser = await _userRepository.UpdatePartialAsync(user)
                ?? throw new ResourceNotFoundException($"User with the id {id} is not found");

            return updatedUser;
        }

        public async Task<IEnumerable<FollowClub>> GetUserFollowingsAsync(int id, int page = 1, int pageSize = 20)
        {
            return await _followService.GetFollowsByUserAsync(id, page, pageSize);
        }
    }
}
