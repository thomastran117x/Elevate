using backend.main.application.security.authorization;
using backend.main.features.clubs.discussions.replies;
using backend.main.features.clubs.moderation;
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
        private readonly IClubModerationLog _moderationLog;

        public const string ContributeForbiddenMessage = "You must be a member of this club to start a discussion.";

        public ClubDiscussionService(
            IClubDiscussionRepository discussionRepository,
            IResourceAuthorizer authorizer,
            IUserRepository userRepository,
            IClubModerationLog moderationLog)
        {
            _discussionRepository = discussionRepository;
            _authorizer = authorizer;
            _userRepository = userRepository;
            _moderationLog = moderationLog;
        }

        public async Task<ClubDiscussion> CreateAsync(int clubId, int userId, string? userRole, string title, string description)
        {
            // Authoring needs membership in public and private clubs alike, unlike reading.
            // A missing club is reported as not found.
            await EnsureCanContributeAsync(clubId, userId, userRole);

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

        public async Task<ClubDiscussion> UpdateAsync(
            int clubId, int discussionId, int userId, string? userRole, string title, string description)
        {
            // Authors keep their threads only while they still belong to the club.
            await EnsureCanContributeAsync(clubId, userId, userRole);

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

        public async Task DeleteAsync(int clubId, int discussionId, int userId, string? userRole, string? reason = null)
        {
            await EnsureCanContributeAsync(clubId, userId, userRole);

            var discussion = await _discussionRepository.GetByIdAsync(discussionId)
                ?? throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            if (discussion.ClubId != clubId)
                throw new ResourceNotFoundException($"Discussion with ID {discussionId} was not found.");

            // The author, staff who outrank the author in this club, or an admin.
            var resource = new ClubContentResource(
                clubId, ClubModerationContentTypes.Discussion, discussion.Id, discussion.UserId);
            var decision = await _authorizer.AuthorizeContentAsync(
                ResourceActor.User(userId, userRole),
                Permissions.ContentRemove,
                resource,
                new PermissionMessages("You are not allowed to delete this discussion."));

            if (decision.IsModeration)
            {
                var moderationReason = ClubModeration.RequireReason(reason);
                // Staged before the delete so it commits in the repository's save.
                _moderationLog.Stage(ClubModeration.Entry(
                    resource, userId, decision, ClubModerationActionTypes.Remove, discussion.Title, moderationReason));
            }

            await _discussionRepository.DeleteAsync(discussionId);
        }

        private Task EnsureCanContributeAsync(int clubId, int userId, string? userRole) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, userRole),
                Permissions.ClubContribute,
                new ClubResource(clubId),
                new PermissionMessages(ContributeForbiddenMessage));

        private Task EnsureAuthorAsync(ClubDiscussion discussion, int userId, string action) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, null),
                Permissions.ContentAuthor,
                new AuthoredContentResource("Discussion", discussion.Id, discussion.UserId),
                new PermissionMessages($"You are not allowed to {action} this discussion."));
    }
}
