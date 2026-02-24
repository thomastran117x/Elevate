using System.Text.Json;

using backend.main.Exceptions;
using backend.main.Interfaces;
using backend.main.Models;
using backend.main.Utilities;

namespace backend.main.Services
{
    public class FollowService : IFollowService
    {
        private readonly IFollowRepository _followRepository;
        private readonly ICacheService _cache;

        private static readonly TimeSpan FollowTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FollowIdTTL = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ListTTL = TimeSpan.FromMinutes(3);

        public FollowService(
            IFollowRepository followRepository,
            ICacheService cache)
        {
            _followRepository = followRepository;
            _cache = cache;
        }

        private string FollowKey(int userId, int clubId)
            => $"follow:u:{userId}:c:{clubId}";

        private string FollowIdKey(int id)
            => $"follow:id:{id}";

        private string UserFollowsKey(int userId, int page, int size)
            => $"follow:list:u:{userId}:{page}:{size}";

        private string ClubFollowsKey(int clubId, int page, int size)
            => $"follow:list:c:{clubId}:{page}:{size}";

        public async Task<FollowClub> GetFollowAsync(int id)
        {
            try
            {
                var key = FollowIdKey(id);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return JsonSerializer.Deserialize<FollowClub>(cached)!;

                var follow = await _followRepository.GetFollowAsync(id);
                if (follow == null)
                    throw new NotFoundException($"Following with id {id} not found");

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(follow), FollowIdTTL);
                return follow;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[FollowService] GetFollowAsync failed: {e}");
                throw new InternalServerException();
            }
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                var key = UserFollowsKey(userId, page, pageSize);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return JsonSerializer.Deserialize<List<FollowClub>>(cached)!;

                var follows = (await _followRepository.GetFollowsByUserAsync(userId, page, pageSize)).ToList();

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(follows), ListTTL);
                return follows;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[FollowService] GetFollowsByUserAsync failed: {e}");
                throw new InternalServerException();
            }
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20)
        {
            try
            {
                var key = ClubFollowsKey(clubId, page, pageSize);
                var cached = await _cache.GetValueAsync(key);

                if (cached != null)
                    return JsonSerializer.Deserialize<List<FollowClub>>(cached)!;

                var follows = (await _followRepository.GetFollowsByClubAsync(clubId, page, pageSize)).ToList();

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(follows), ListTTL);
                return follows;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[FollowService] GetFollowsByClubAsync failed: {e}");
                throw new InternalServerException();
            }
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20)
        {
            try
            {
                var key = $"follow:list:all:{page}:{pageSize}";

                var cached = await _cache.GetValueAsync(key);
                if (cached != null)
                    return JsonSerializer.Deserialize<List<FollowClub>>(cached)!;

                var follows = (await _followRepository.GetFollowsAsync(page, pageSize)).ToList();

                await _cache.SetValueAsync(key, JsonSerializer.Serialize(follows), TimeSpan.FromMinutes(3));
                return follows;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[FollowService] GetFollowsAsync failed: {e}");
                throw new InternalServerException();
            }
        }

        public async Task<bool> IsMemberAsync(int clubId, int userId)
        {
            return await _followRepository.IsFollowingClubAsync(clubId, userId) != null;
        }

        public async Task AddMembershipAsync(int clubId, int userId)
        {
            await _followRepository.FollowClubAsync(userId, clubId);
        }

        public async Task RemoveMembershipAsync(int clubId, int userId)
        {
            await _followRepository.UnfollowClubAsync(clubId, userId);
        }


    }
}
