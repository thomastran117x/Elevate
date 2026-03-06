using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;


namespace backend.main.services.implementation
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IFileUploadService _fileService;
        private readonly IFollowService _followService;
        public UserService(IUserRepository userRepository, IFileUploadService fileService, IFollowService followService)
        {
            _userRepository = userRepository;
            _fileService = fileService;
            _followService = followService;
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return (List<User>)await _userRepository.GetUsersAsync();
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
