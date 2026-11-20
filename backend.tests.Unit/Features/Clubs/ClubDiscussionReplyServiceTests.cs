using backend.main.features.clubs.discussions;
using backend.main.features.clubs.discussions.replies;
using backend.main.features.clubs.moderation;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Support;

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
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectAPrivateClubOutsider()
    {
        var harness = new Harness(isPrivate: true);

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

    [Fact]
    public async Task DeleteAsync_ShouldRefuseANonStaffCaller_OnSomeoneElsesReply()
    {
        var harness = new Harness(isPrivate: false);
        harness.Replies.Setup(r => r.GetByIdAsync(40)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 40,
            DiscussionId = harness.DiscussionId,
            UserId = harness.MemberAuthorId,
            Content = "Hello"
        });

        var act = () => harness.Service.DeleteAsync(
            harness.ClubId, harness.DiscussionId, 40, harness.UserId, "Participant", "Spam");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to delete this reply.");
        harness.Replies.Verify(r => r.SoftDeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRequireAReason_WhenStaffRemoveAMembersReply()
    {
        var harness = new Harness(isPrivate: true);
        harness.Access.Manager(harness.ClubId, harness.UserId);
        harness.Replies.Setup(r => r.GetByIdAsync(41)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 41,
            DiscussionId = harness.DiscussionId,
            UserId = harness.MemberAuthorId,
            Content = "Buy now"
        });

        var act = () => harness.Service.DeleteAsync(
            harness.ClubId, harness.DiscussionId, 41, harness.UserId, "Participant");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ClubModeration.ReasonRequiredMessage);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveAndLog_WhenAManagerRemovesAVolunteersReply()
    {
        var harness = new Harness(isPrivate: true);
        harness.Access.Manager(harness.ClubId, harness.UserId);
        harness.Access.Volunteer(harness.ClubId, harness.MemberAuthorId);
        harness.Replies.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 42,
            DiscussionId = harness.DiscussionId,
            UserId = harness.MemberAuthorId,
            Content = "Off topic"
        });
        harness.Replies.Setup(r => r.SoftDeleteAsync(42)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 42,
            DiscussionId = harness.DiscussionId,
            UserId = harness.MemberAuthorId,
            IsDeleted = true
        });
        harness.SetupEmptyViewData(42);
        ClubModerationAction? staged = null;
        harness.ModerationLog.Setup(log => log.Stage(It.IsAny<ClubModerationAction>()))
            .Callback((ClubModerationAction entry) => staged = entry);

        var view = await harness.Service.DeleteAsync(
            harness.ClubId, harness.DiscussionId, 42, harness.UserId, "Participant", "Spam");

        view.Reply.IsDeleted.Should().BeTrue();
        staged.Should().NotBeNull();
        staged!.ContentType.Should().Be(ClubModerationContentTypes.Reply);
        staged.ActorClubRole.Should().Be(ClubModerationRoles.Manager);
        staged.ContentSnapshot.Should().Be("Off topic");
        staged.Reason.Should().Be("Spam");
    }

    [Fact]
    public async Task DeleteAsync_ShouldLetTheAuthorRemoveTheirReply_WithoutLogging()
    {
        var harness = new Harness(isPrivate: false);
        harness.Replies.Setup(r => r.GetByIdAsync(43)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 43,
            DiscussionId = harness.DiscussionId,
            UserId = harness.UserId,
            Content = "Mine"
        });
        harness.Replies.Setup(r => r.SoftDeleteAsync(43)).ReturnsAsync(new ClubDiscussionReply
        {
            Id = 43,
            DiscussionId = harness.DiscussionId,
            UserId = harness.UserId,
            IsDeleted = true
        });
        harness.SetupEmptyViewData(43);

        await harness.Service.DeleteAsync(harness.ClubId, harness.DiscussionId, 43, harness.UserId, "Participant");

        harness.ModerationLog.Verify(log => log.Stage(It.IsAny<ClubModerationAction>()), Times.Never);
    }

    private sealed class Harness
    {
        public Mock<IClubDiscussionReplyRepository> Replies { get; } = new();
        public Mock<IClubDiscussionRepository> Discussions { get; } = new();
        public FakeClubAuthorizer Access { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IClubModerationLog> ModerationLog { get; } = new();
        public ClubDiscussionReplyService Service { get; }
        public int ClubId => 7;
        public int DiscussionId => 8;
        public int UserId => 9;

        /// <summary>A member with no staff role, unless a test grants one.</summary>
        public int MemberAuthorId => 999;

        public Harness(bool isPrivate)
        {
            Access.Club(ClubId, isPrivate, ownerId: 1);
            Discussions.Setup(r => r.GetByIdAsync(DiscussionId)).ReturnsAsync(new ClubDiscussion
            {
                Id = DiscussionId,
                ClubId = ClubId,
                UserId = 1,
                Title = "Topic",
                Description = "Body"
            });
            Access.Member(ClubId, MemberAuthorId);
            Service = new ClubDiscussionReplyService(
                Replies.Object, Discussions.Object, Access, Users.Object, ModerationLog.Object);
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
