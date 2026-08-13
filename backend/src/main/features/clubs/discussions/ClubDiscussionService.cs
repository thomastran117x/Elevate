using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.discussions
{
    public class ClubDiscussionService : IClubDiscussionService
    {
        private readonly IClubDiscussionRepository _discussionRepository;
        private readonly IClubService _clubService;
        private readonly IFollowRepository _followRepository;
        private readonly IUserRepository _userRepository;

        public ClubDiscussionService(
            IClubDiscussionRepository discussionRepository,
            IClubService clubService,
            IFollowRepository followRepository,
            IUserRepository userRepository)
        {
            _discussionRepository = discussionRepository;
            _clubService = clubService;
            _followRepository = followRepository;
            _userRepository = userRepository;
        }

        public async Task<ClubDiscussion> CreateAsync(int clubId, int userId, string? userRole, string title, string description)
        {
            // Throws ResourceNotFoundException when the club does not exist.
            await _clubService.GetClub(clubId);

            await EnsureCanWriteAsync(clubId, userId, userRole);

            var discussion = new ClubDiscussion
            {
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Description = description
            };

            return await _discussionRepository.CreateAsync(discussion);
        }

        public async Task<(IReadOnlyList<ClubDiscussion> Discussions, IReadOnlyDictionary<int, UserListRecord> Authors, int TotalCount)>
            GetByClubIdAsync(int clubId, int? requestingUserId, string? requestingUserRole, int page, int pageSize)
        {
            var club = await _clubService.GetClub(clubId);

            await EnsureCanReadAsync(club, requestingUserId, requestingUserRole);

            var discussions = await _discussionRepository.GetByClubIdAsync(clubId, page, pageSize);
            var totalCount = await _discussionRepository.CountByClubIdAsync(clubId);

            var userIds = discussions.Select(d => d.UserId).Distinct().ToList();
            IReadOnlyDictionary<int, UserListRecord> authors = userIds.Count == 0
                ? new Dictionary<int, UserListRecord>()
                : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(u => u.Id);

            return (discussions, authors, totalCount);
        }

        public async Task<ClubDiscussion> UpdateAsync(int clubId, int discussionId, int userId, string title, string description)
        {
            var discussion = await _discussionRepository.GetByIdAsync(discussionId)
                ?? throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            // A club mismatch reads as "not found" so a wrong club id cannot be used to probe existence.
            if (discussion.ClubId != clubId)
                throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            if (discussion.UserId != userId)
                throw new ForbiddenException("You are not allowed to update this discussion.");

            return await _discussionRepository.UpdateAsync(discussionId, new ClubDiscussion
            {
                Title = title,
                Description = description
            }) ?? throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");
        }

        public async Task DeleteAsync(int clubId, int discussionId, int userId)
        {
            var discussion = await _discussionRepository.GetByIdAsync(discussionId)
                ?? throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            if (discussion.ClubId != clubId)
                throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            if (discussion.UserId != userId)
                throw new ForbiddenException("You are not allowed to delete this discussion.");

            await _discussionRepository.DeleteAsync(discussionId);
        }

        /// <summary>
        /// Public clubs are readable by anyone. Private clubs are readable only by members and staff.
        /// </summary>
        private async Task EnsureCanReadAsync(Club club, int? userId, string? userRole)
        {
            if (!club.isPrivate)
                return;

            if (userId == null)
                throw new UnauthorizedException("Authentication is required to view discussions for a private club.");

            if (await IsMemberOrStaffAsync(club.Id, userId.Value, userRole))
                return;

            throw new ForbiddenException("You must be a member of this club to view its discussions.");
        }

        /// <summary>
        /// Authoring is gated on membership for public and private clubs alike — unlike reading,
        /// a public club still requires the author to have joined.
        /// </summary>
        private async Task EnsureCanWriteAsync(int clubId, int userId, string? userRole)
        {
            if (await IsMemberOrStaffAsync(clubId, userId, userRole))
                return;

            throw new ForbiddenException("You must be a member of this club to start a discussion.");
        }

        private async Task<bool> IsMemberOrStaffAsync(int clubId, int userId, string? userRole)
        {
            if (await _clubService.HasClubStaffAccessAsync(clubId, userId, userRole))
                return true;

            return await _followRepository.IsFollowingClubAsync(clubId, userId) != null;
        }
    }
}
