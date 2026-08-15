using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;

using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.comments.contracts.requests;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class PostCommentControllerTests
{
    [Fact]
    public async Task GetComments_ShouldClampPagingAndMapAnAnonymousPage()
    {
        var service = new Mock<IPostCommentService>();
        var view = CreateView();
        service.Setup(s => s.GetPageAsync(
                4, 8, null, PostCommentSort.Newest, "cursor", 100, null, null))
            .ReturnsAsync(new PostCommentPage([view], 3, "next", true));
        var controller = CreateController(service.Object, new CommentEventBroker(), authenticated: false);

        var result = await controller.GetComments(4, 8, null, PostCommentSort.Newest, "cursor", 500);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PostCommentPageResponse>>().Subject;
        response.Data!.TotalCount.Should().Be(3);
        response.Data.NextCursor.Should().Be("next");
        response.Data.HasMore.Should().BeTrue();
        var item = response.Data.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(12);
        item.Author!.Name.Should().Be("Taylor Rider");
        item.CurrentUserReaction.Should().Be("Like");
        item.DirectReplyCount.Should().Be(4);
    }

    [Fact]
    public async Task CreateAndUpdate_ShouldReturnCompleteCommentsAndPublishEvents()
    {
        var service = new Mock<IPostCommentService>();
        var broker = new CommentEventBroker();
        var events = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(8, Guid.NewGuid(), events.Writer);
        var createdView = CreateView(parentCommentId: 5);
        var updatedView = CreateView(content: "Updated comment");
        service.Setup(s => s.CreateAsync(4, 8, 5, 7, "Participant", "New comment"))
            .ReturnsAsync(createdView);
        service.Setup(s => s.UpdateAsync(4, 8, 12, 7, "Participant", "Updated comment"))
            .ReturnsAsync(updatedView);
        var controller = CreateController(service.Object, broker);

        var createResult = await controller.CreateComment(4, 8, new PostCommentCreateRequest
        {
            ParentCommentId = 5,
            Content = "New comment"
        });
        var created = createResult.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeOfType<ApiResponse<PostCommentResponse>>()
            .Which.Data!.ParentCommentId.Should().Be(5);
        events.Reader.TryRead(out var createdEvent).Should().BeTrue();
        createdEvent!.Type.Should().Be("CommentCreated");

        var updateResult = await controller.UpdateComment(4, 8, 12, new PostCommentUpdateRequest
        {
            Content = "Updated comment"
        });
        var updated = updateResult.Should().BeOfType<OkObjectResult>().Subject;
        updated.Value.Should().BeOfType<ApiResponse<PostCommentResponse>>()
            .Which.Data!.Content.Should().Be("Updated comment");
        events.Reader.TryRead(out var updatedEvent).Should().BeTrue();
        updatedEvent!.Type.Should().Be("CommentUpdated");
        updatedEvent.Payload.Should().BeOfType<PostCommentResponse>()
            .Which.CurrentUserReaction.Should().BeNull();
    }

    [Fact]
    public async Task DeleteComment_ShouldReturnAndBroadcastADeletedPlaceholder()
    {
        var service = new Mock<IPostCommentService>();
        var broker = new CommentEventBroker();
        var events = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(8, Guid.NewGuid(), events.Writer);
        service.Setup(s => s.DeleteAsync(4, 8, 12, 7, "Participant"))
            .ReturnsAsync(CreateView(isDeleted: true));
        var controller = CreateController(service.Object, broker);

        var result = await controller.DeleteComment(4, 8, 12);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PostCommentResponse>>().Subject;
        response.Data!.IsDeleted.Should().BeTrue();
        response.Data.UserId.Should().BeNull();
        response.Data.Content.Should().BeNull();
        response.Data.Author.Should().BeNull();
        events.Reader.TryRead(out var deletedEvent).Should().BeTrue();
        deletedEvent!.Type.Should().Be("CommentDeleted");
    }

    [Fact]
    public async Task ReactionEndpoints_ShouldReturnViewerStateAndBroadcastAggregates()
    {
        var service = new Mock<IPostCommentService>();
        var broker = new CommentEventBroker();
        var events = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(8, Guid.NewGuid(), events.Writer);
        service.Setup(s => s.SetReactionAsync(
                4, 8, 12, 7, "Participant", PostCommentReactionType.Dislike))
            .ReturnsAsync(new PostCommentReactionSummary(2, 3, PostCommentReactionType.Dislike));
        service.Setup(s => s.ClearReactionAsync(4, 8, 12, 7, "Participant"))
            .ReturnsAsync(new PostCommentReactionSummary(2, 2, null));
        var controller = CreateController(service.Object, broker);

        var setResult = await controller.SetReaction(4, 8, 12, new PostCommentReactionRequest
        {
            Reaction = PostCommentReactionType.Dislike
        });
        var setResponse = setResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<PostCommentReactionResponse>>().Subject;
        setResponse.Data!.CurrentUserReaction.Should().Be("Dislike");
        setResponse.Data.DislikeCount.Should().Be(3);
        events.Reader.TryRead(out var setEvent).Should().BeTrue();
        setEvent!.Type.Should().Be("CommentReactionChanged");
        JsonSerializer.Serialize(setEvent.Payload).Should().Contain("\"dislikeCount\":3");

        var clearResult = await controller.ClearReaction(4, 8, 12);
        var clearResponse = clearResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<PostCommentReactionResponse>>().Subject;
        clearResponse.Data!.CurrentUserReaction.Should().BeNull();
        clearResponse.Data.DislikeCount.Should().Be(2);
        events.Reader.TryRead(out var clearEvent).Should().BeTrue();
        clearEvent!.Type.Should().Be("CommentReactionChanged");
    }

    [Fact]
    public void StreamComments_ShouldDisableTheGlobalRequestTimeout()
    {
        var action = typeof(PostCommentController)
            .GetMethod(nameof(PostCommentController.StreamComments));

        action.Should().NotBeNull();
        action!.GetCustomAttribute<DisableRequestTimeoutAttribute>().Should().NotBeNull();
    }

    private static PostCommentController CreateController(
        IPostCommentService service,
        CommentEventBroker broker,
        bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "member@example.com"),
                    new Claim(ClaimTypes.Role, "Participant")
                ], "TestAuth")
            : new ClaimsIdentity();

        return new PostCommentController(service, broker)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static PostCommentView CreateView(
        int? parentCommentId = null,
        string content = "Post comment",
        bool isDeleted = false)
    {
        var createdAt = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        return new PostCommentView(
            new PostComment
            {
                Id = 12,
                PostId = 8,
                ParentCommentId = parentCommentId,
                UserId = 7,
                Content = isDeleted ? string.Empty : content,
                IsDeleted = isDeleted,
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddMinutes(2)
            },
            isDeleted ? null : new UserListRecord
            {
                Id = 7,
                Email = "member@example.com",
                Username = "taylor",
                Name = "Taylor Rider",
                Usertype = "Participant"
            },
            isDeleted ? 0 : 2,
            isDeleted ? 0 : 1,
            isDeleted ? null : PostCommentReactionType.Like,
            4);
    }
}
