using backend.main.application.security.authorization;
using backend.main.features.clubs.discussions.replies;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.discussions
{
    public class ClubDiscussionService : IClubDiscussionService
    {
        private readonly IClubDiscussionRepository _discussionRepository;
        private readonly IResourceAuthorizer _authorizer;
        private readonly IUserRepository _userRepository;

        public const string ContributeForbiddenMessage = "You must be a member of this club to start a discussion.";

        public ClubDiscussionService(
            IClubDiscussionRepository discussionRepository,
            IResourceAuthorizer authorizer,
            IUserRepository userRepository)
        {
            _discussionRepository = discussionRepository;
            _authorizer = authorizer;
            _userRepository = userRepository;
        }

        public async Task<ClubDiscussion> CreateAsync(int clubId, int userId, string? userRole, string title, string description)
        {
            // Authoring needs membership in public and private clubs alike, unlike reading.
            // A missing club is reported as not found.
            await _authorizer.EnsureAsync(
                ResourceActor.User(userId, userRole),
                Permissions.ClubContribute,
                new ClubResource(clubId),
                new PermissionMessages(ContributeForbiddenMessage));

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
            await _authorizer.EnsureAsync(
                new ResourceActor(requestingUserId, requestingUserRole),
                Permissions.ClubRead,
                new ClubResource(clubId),
                new PermissionMessages(
                    ClubDiscussionReplyService.ReadForbiddenMessage,
                    ClubDiscussionReplyService.ReadAnonymousMessage));

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

            await EnsureAuthorAsync(discussion, userId, "update");

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

            await EnsureAuthorAsync(discussion, userId, "delete");

            await _discussionRepository.DeleteAsync(discussionId);
        }

        private Task EnsureAuthorAsync(ClubDiscussion discussion, int userId, string action) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, null),
                Permissions.ContentAuthor,
                new AuthoredContentResource("Discussion", discussion.Id, discussion.UserId),
                new PermissionMessages($"You are not allowed to {action} this discussion."));
    }
}
