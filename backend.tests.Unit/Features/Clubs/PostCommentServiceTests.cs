using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class PostCommentServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldAllowAnyAuthenticatedUserInAPublicClub()
    {
        var harness = new Harness(isPrivate: false);
        harness.Comments.Setup(repository => repository.CreateAsync(It.IsAny<PostComment>()))
            .ReturnsAsync((PostComment comment) => { comment.Id = 10; return comment; });
        harness.SetupEmptyViewData(10);

        var created = await harness.Service.CreateAsync(
            harness.ClubId, harness.PostId, null, harness.UserId, "Participant", "  Hello  ");

        created.Comment.Content.Should().Be("Hello");
        harness.Follows.Verify(
            repository => repository.IsFollowingClubAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectAPrivateClubOutsider()
    {
        var harness = new Harness(isPrivate: true);
        harness.Clubs.Setup(service => service.HasClubStaffAccessAsync(
            harness.ClubId, harness.UserId, It.IsAny<string?>())).ReturnsAsync(false);
        harness.Follows.Setup(repository => repository.IsFollowingClubAsync(
            harness.ClubId, harness.UserId)).ReturnsAsync((FollowClub?)null);

        var act = () => harness.Service.CreateAsync(
            harness.ClubId, harness.PostId, null, harness.UserId, "Participant", "Hello");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to participate in its comments.");
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectADeletedParentAtAnyDepth()
    {
        var harness = new Harness(isPrivate: false);
        harness.Comments.Setup(repository => repository.GetByIdAsync(20)).ReturnsAsync(new PostComment
        {
            Id = 20,
            PostId = harness.PostId,
            UserId = 2,
            IsDeleted = true
        });

        var act = () => harness.Service.CreateAsync(
            harness.ClubId, harness.PostId, 20, harness.UserId, "Participant", "Child");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Deleted comments cannot receive new replies.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectANonAuthorAndDeletedCommentReactions()
    {
        var harness = new Harness(isPrivate: false);
        harness.Comments.Setup(repository => repository.GetByIdAsync(30)).ReturnsAsync(new PostComment
        {
            Id = 30,
            PostId = harness.PostId,
            UserId = 999
        });
        var edit = () => harness.Service.UpdateAsync(
            harness.ClubId, harness.PostId, 30, harness.UserId, "Participant", "Edit");
        await edit.Should().ThrowAsync<ForbiddenException>();

        harness.Comments.Setup(repository => repository.GetByIdAsync(31)).ReturnsAsync(new PostComment
        {
            Id = 31,
            PostId = harness.PostId,
            UserId = harness.UserId,
            IsDeleted = true
        });
        var react = () => harness.Service.SetReactionAsync(
            harness.ClubId, harness.PostId, 31, harness.UserId, "Participant",
            PostCommentReactionType.Like);
        await react.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Deleted comments cannot be reacted to.");
    }

    [Fact]
    public async Task GetPageAsync_ShouldRejectAPostFromAnotherClub()
    {
        var harness = new Harness(isPrivate: false, postClubId: 99);

        var act = () => harness.Service.GetPageAsync(
            harness.ClubId, harness.PostId, null, PostCommentSort.Newest,
            null, 20, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Post with ID {harness.PostId} was not found.");
    }

    private sealed class Harness
    {
        public Mock<IPostCommentRepository> Comments { get; } = new();
        public Mock<IClubPostRepository> Posts { get; } = new();
        public Mock<IClubService> Clubs { get; } = new();
        public Mock<IFollowRepository> Follows { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public PostCommentService Service { get; }
        public int ClubId => 7;
        public int PostId => 8;
        public int UserId => 9;

        public Harness(bool isPrivate, int? postClubId = null)
        {
            Clubs.Setup(service => service.GetClub(ClubId)).ReturnsAsync(new Club
            {
                Id = ClubId,
                UserId = 1,
                Name = "Club",
                Description = "Description",
                Clubtype = ClubType.Gaming,
                ClubImage = "image.png",
                isPrivate = isPrivate
            });
            Posts.Setup(repository => repository.GetByIdAsync(PostId)).ReturnsAsync(new ClubPost
            {
                Id = PostId,
                ClubId = postClubId ?? ClubId,
                UserId = 1,
                Title = "Post",
                Content = "Body"
            });
            Service = new PostCommentService(
                Comments.Object, Posts.Object, Clubs.Object, Follows.Object, Users.Object);
        }

        public void SetupEmptyViewData(int commentId)
        {
            Comments.Setup(repository => repository.GetDirectReplyCountsAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == commentId)))
                .ReturnsAsync([]);
            Comments.Setup(repository => repository.GetReactionSummariesAsync(
                It.Is<IEnumerable<int>>(ids => ids.Single() == commentId), UserId))
                .ReturnsAsync(new Dictionary<int, PostCommentReactionSummary>
                {
                    [commentId] = new(0, 0, null)
                });
            Users.Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync([]);
        }
    }
}
