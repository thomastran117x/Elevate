using backend.main.features.clubs;
using backend.main.features.clubs.discussions;
using backend.main.features.clubs.discussions.replies;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionReplyServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldAllowAnyAuthenticatedUserInAPublicClub()
    {
        var harness = new Harness(isPrivate: false);
        harness.Replies.Setup(r => r.CreateAsync(It.IsAny<ClubDiscussionReply>()))
            .ReturnsAsync((ClubDiscussionReply reply) => { reply.Id = 10; return reply; });
        harness.SetupEmptyViewData(10);

        var created = await harness.Service.CreateAsync(
            harness.ClubId, harness.DiscussionId, null, harness.UserId, "Participant", "  Hello  ");

        created.Reply.Content.Should().Be("Hello");
        harness.Follows.Verify(
            r => r.IsFollowingClubAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectAPrivateClubOutsider()
    {
        var harness = new Harness(isPrivate: true);
        harness.Clubs.Setup(s => s.HasClubStaffAccessAsync(
            harness.ClubId, harness.UserId, It.IsAny<string?>())).ReturnsAsync(false);
        harness.Follows.Setup(r => r.IsFollowingClubAsync(harness.ClubId, harness.UserId))
            .ReturnsAsync((FollowClub?)null);

        var act = () => harness.Service.CreateAsync(
            harness.ClubId, harness.DiscussionId, null, harness.UserId, "Participant", "Hello");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to participate in its discussions.");
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectADeletedParentAtAnyDepth()
    {
        var harness = new Harness(isPrivate: false);
        harness.Replies.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 20,
            DiscussionId = harness.DiscussionId,
            UserId = 2,
            IsDeleted = true
        });

        var act = () => harness.Service.CreateAsync(
            harness.ClubId, harness.DiscussionId, 20, harness.UserId, "Participant", "Child");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Deleted replies cannot receive new replies.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectANonAuthorAndDeletedReplyReactions()
    {
        var harness = new Harness(isPrivate: false);
        harness.Replies.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 30,
            DiscussionId = harness.DiscussionId,
            UserId = 999
        });
        var edit = () => harness.Service.UpdateAsync(
            harness.ClubId, harness.DiscussionId, 30, harness.UserId, "Participant", "Edit");
        await edit.Should().ThrowAsync<ForbiddenException>();

        harness.Replies.Setup(r => r.GetByIdAsync(31)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 31,
            DiscussionId = harness.DiscussionId,
            UserId = harness.UserId,
            IsDeleted = true
        });
        var react = () => harness.Service.SetReactionAsync(
            harness.ClubId, harness.DiscussionId, 31, harness.UserId, "Participant",
            DiscussionReplyReactionType.Like);
        await react.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Deleted replies cannot be reacted to.");
    }

    private sealed class Harness
    {
        public Mock<IClubDiscussionReplyRepository> Replies { get; } = new();
        public Mock<IClubDiscussionRepository> Discussions { get; } = new();
        public Mock<IClubService> Clubs { get; } = new();
        public Mock<IFollowRepository> Follows { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public ClubDiscussionReplyService Service { get; }
        public int ClubId => 7;
        public int DiscussionId => 8;
        public int UserId => 9;

        public Harness(bool isPrivate)
        {
            Clubs.Setup(s => s.GetClub(ClubId)).ReturnsAsync(new Club
            {
                Id = ClubId,
                UserId = 1,
                Name = "Club",
                Description = "Description",
                Clubtype = ClubType.Gaming,
                ClubImage = "image.png",
                isPrivate = isPrivate
            });
            Discussions.Setup(r => r.GetByIdAsync(DiscussionId)).ReturnsAsync(new ClubDiscussion
            {
                Id = DiscussionId,
                ClubId = ClubId,
                UserId = 1,
                Title = "Topic",
                Description = "Body"
            });
            Service = new ClubDiscussionReplyService(
                Replies.Object, Discussions.Object, Clubs.Object, Follows.Object, Users.Object);
        }

        public void SetupEmptyViewData(int replyId)
        {
            Replies.Setup(r => r.GetDirectReplyCountsAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == replyId)))
                .ReturnsAsync([]);
            Replies.Setup(r => r.GetReactionSummariesAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == replyId), UserId))
                .ReturnsAsync(new Dictionary<int, DiscussionReplyReactionSummary>
                {
                    [replyId] = new(0, 0, null)
                });
            Users.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync([]);
        }
    }
}
