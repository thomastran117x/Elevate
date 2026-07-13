using backend.main.features.cache;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.follow
{
    public class FollowService : IFollowService
    {
        private readonly IFollowRepository _followRepository;
        private readonly IUserRepository _userRepository;
        private readonly ICacheService _cache;
        private readonly IRefreshAheadCache _refreshCache;

        private static readonly TimeSpan FollowTTL = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FollowIdTTL = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ListTTL = TimeSpan.FromMinutes(3);

        public FollowService(
            IFollowRepository followRepository,
            IUserRepository userRepository,
            ICacheService cache,
            IRefreshAheadCache refreshCache)
        {
            _followRepository = followRepository;
            _userRepository = userRepository;
            _cache = cache;
            _refreshCache = refreshCache;
        }

        private string FollowKey(int userId, int clubId) => $"follow:u:{userId}:c:{clubId}";
        private string FollowIdKey(int id) => $"follow:id:{id}";
        private string UserFollowsKey(int userId, int page, int size) => $"follow:list:u:{userId}:{page}:{size}";
        private string ClubFollowsKey(int clubId, int page, int size) => $"follow:list:c:{clubId}:{page}:{size}";

        public async Task<FollowClub> GetFollowAsync(int id)
        {
            var follow = await _refreshCache.GetOrSetAsync(
                FollowIdKey(id),
                () => _followRepository.GetFollowAsync(id),
                FollowIdTTL);

            return follow ?? throw new ResourceNotFoundException($"Following with id {id} not found");
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            return await _refreshCache.GetOrSetAsync(
                UserFollowsKey(userId, page, pageSize),
                async () => (await _followRepository.GetFollowsByUserAsync(userId, page, pageSize)).ToList(),
                ListTTL) ?? [];
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20)
        {
            return await _refreshCache.GetOrSetAsync(
                ClubFollowsKey(clubId, page, pageSize),
                async () => (await _followRepository.GetFollowsByClubAsync(clubId, page, pageSize)).ToList(),
                ListTTL) ?? [];
        }

        public async Task<(IReadOnlyList<FollowClub> Members, IReadOnlyDictionary<int, UserListRecord> Users, int TotalCount)>
            GetClubMembersAsync(int clubId, int page = 1, int pageSize = 20)
        {
            var members = (await GetFollowsByClubAsync(clubId, page, pageSize)).ToList();
            var totalCount = await _followRepository.CountFollowsByClubAsync(clubId);

            var userIds = members.Select(m => m.UserId).Distinct().ToList();
            IReadOnlyDictionary<int, UserListRecord> users = userIds.Count == 0
                ? new Dictionary<int, UserListRecord>()
                : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(u => u.Id);

            return (members, users, totalCount);
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20)
        {
            return await _refreshCache.GetOrSetAsync(
                $"follow:list:all:{page}:{pageSize}",
                async () => (await _followRepository.GetFollowsAsync(page, pageSize)).ToList(),
                ListTTL) ?? [];
        }

        public async Task<bool> IsMemberAsync(int clubId, int userId)
        {
            var follow = await _refreshCache.GetOrSetAsync(
                FollowKey(userId, clubId),
                () => _followRepository.IsFollowingClubAsync(clubId, userId),
                FollowTTL);

            return follow != null;
        }

        public async Task AddMembershipAsync(int clubId, int userId)
        {
            var follow = await _followRepository.FollowClubAsync(clubId, userId);
            await _refreshCache.SetAsync(FollowKey(userId, clubId), follow, FollowTTL);
            await InvalidateFollowListsAsync(userId, clubId);
        }

        public async Task RemoveMembershipAsync(int clubId, int userId)
        {
            await _followRepository.UnfollowClubAsync(clubId, userId);
            await _refreshCache.RemoveAsync(FollowKey(userId, clubId));
            await InvalidateFollowListsAsync(userId, clubId);
        }

        private async Task InvalidateFollowListsAsync(int userId, int clubId)
        {
            // ScanKeys requires raw ICacheService — no abstraction equivalent needed
            var server = _cache.GetServer();
            var userListKeys = _cache.ScanKeys(server, $"follow:list:u:{userId}:*");
            var clubListKeys = _cache.ScanKeys(server, $"follow:list:c:{clubId}:*");

            foreach (var key in userListKeys.Concat(clubListKeys))
                await _cache.DeleteKeyAsync(key);
        }
    }
}
