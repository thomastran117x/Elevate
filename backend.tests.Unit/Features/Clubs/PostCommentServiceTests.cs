using backend.main.features.clubs;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class PostCommentServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldRejectWhenPostBelongsToDifferentClub()
    {
        var comments = new Mock<IPostCommentRepository>();
        var posts = new Mock<IClubPostRepository>();
        var clubs = new Mock<IClubRepository>();

        clubs.Setup(repo => repo.GetByIdAsync(4))
            .ReturnsAsync(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Chess Club",
                Description = "Strategy",
                Clubtype = ClubType.Social,
                ClubImage = "https://cdn.test/chess.png"
            });
        posts.Setup(repo => repo.GetByIdAsync(11))
            .ReturnsAsync(new ClubPost
            {
                Id = 11,
                ClubId = 99,
                UserId = 7,
                Title = "News",
                Content = "Hello"
            });

        var service = new PostCommentService(comments.Object, posts.Object, clubs.Object, Mock.Of<IUserRepository>());

        var act = () => service.CreateAsync(4, 11, 20, "Nice update");

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Post with ID 11 was not found.");
    }

    [Fact]
    public async Task GetByPostIdAsync_ShouldReturnItemsAndTotalCount()
    {
        var comments = new Mock<IPostCommentRepository>();
        var posts = new Mock<IClubPostRepository>();
        var clubs = new Mock<IClubRepository>();

        clubs.Setup(repo => repo.GetByIdAsync(4))
            .ReturnsAsync(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Chess Club",
                Description = "Strategy",
                Clubtype = ClubType.Social,
                ClubImage = "https://cdn.test/chess.png"
            });
        posts.Setup(repo => repo.GetByIdAsync(11))
            .ReturnsAsync(new ClubPost
            {
                Id = 11,
                ClubId = 4,
                UserId = 7,
                Title = "News",
                Content = "Hello"
            });
        comments.Setup(repo => repo.GetByPostIdAsync(11, 2, 5))
            .ReturnsAsync([
                new PostComment
                {
                    Id = 9,
                    PostId = 11,
                    UserId = 20,
                    Content = "First"
                }
            ]);
        comments.Setup(repo => repo.CountByPostIdAsync(11))
            .ReturnsAsync(6);

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
            .ReturnsAsync([]);

        var service = new PostCommentService(comments.Object, posts.Object, clubs.Object, userRepository.Object);

        var result = await service.GetByPostIdAsync(4, 11, 2, 5);

        result.TotalCount.Should().Be(6);
        result.Items.Should().ContainSingle();
        result.Items[0].Content.Should().Be("First");
    }

    [Fact]
    public async Task UpdateAsync_ShouldRejectWhenCommentBelongsToAnotherUser()
    {
        var comments = new Mock<IPostCommentRepository>();
        comments.Setup(repo => repo.GetByIdAsync(15))
            .ReturnsAsync(new PostComment
            {
                Id = 15,
                PostId = 11,
                UserId = 99,
                Content = "Original"
            });

        var service = new PostCommentService(
            comments.Object,
            Mock.Of<IClubPostRepository>(),
            Mock.Of<IClubRepository>(),
            Mock.Of<IUserRepository>());

        var act = () => service.UpdateAsync(11, 15, 20, "Updated");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to update this comment.");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRejectWhenCommentDoesNotBelongToPost()
    {
        var comments = new Mock<IPostCommentRepository>();
        comments.Setup(repo => repo.GetByIdAsync(15))
            .ReturnsAsync(new PostComment
            {
                Id = 15,
                PostId = 44,
                UserId = 20,
                Content = "Original"
            });

        var service = new PostCommentService(
            comments.Object,
            Mock.Of<IClubPostRepository>(),
            Mock.Of<IClubRepository>(),
            Mock.Of<IUserRepository>());

        var act = () => service.DeleteAsync(11, 15, 20);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Comment with ID 15 was not found.");
    }
}
