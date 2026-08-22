using System.Security.Claims;

using backend.main.features.clubs.discussions.replies;
using backend.main.features.clubs.discussions.replies.contracts.requests;
using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.features.clubs.realtime;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionReplyControllerTests
{
    [Fact]
    public async Task CreateAndUpdate_ShouldReturnCompleteRepliesAndBroadcastThem()
    {
        var service = new Mock<IClubDiscussionReplyService>();
        var notifier = new Mock<IClubRealtimeNotifier>();
        service.Setup(s => s.CreateAsync(4, 9, 5, 7, "Participant", "New reply"))
            .ReturnsAsync(CreateView(parentReplyId: 5));
        service.Setup(s => s.UpdateAsync(4, 9, 12, 7, "Participant", "Updated reply"))
            .ReturnsAsync(CreateView(content: "Updated reply"));
        var controller = CreateController(service.Object, notifier.Object);

        var createResult = await controller.CreateReply(4, 9, new DiscussionReplyCreateRequest
        {
            ParentReplyId = 5,
            Content = "New reply"
        });
        var created = createResult.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeOfType<ApiResponse<DiscussionReplyResponse>>()
            .Which.Data!.ParentReplyId.Should().Be(5);
        notifier.Verify(
            n => n.ReplyCreatedAsync(4, It.Is<DiscussionReplyResponse>(r => r.ParentReplyId == 5)),
            Times.Once);

        var updateResult = await controller.UpdateReply(4, 9, 12, new DiscussionReplyUpdateRequest
        {
            Content = "Updated reply"
        });
        updateResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<DiscussionReplyResponse>>()
            .Which.Data!.CurrentUserReaction.Should().Be("Like");

        // The broadcast strips the editor's own reaction; the caller's own body keeps it.
        notifier.Verify(
            n => n.ReplyUpdatedAsync(4, It.Is<DiscussionReplyResponse>(r => r.CurrentUserReaction == null)),
            Times.Once);
    }

    [Fact]
    public async Task DeleteReply_ShouldReturnAndBroadcastADeletedPlaceholder()
    {
        var service = new Mock<IClubDiscussionReplyService>();
        var notifier = new Mock<IClubRealtimeNotifier>();
        service.Setup(s => s.DeleteAsync(4, 9, 12, 7, "Participant"))
            .ReturnsAsync(CreateView(isDeleted: true));
        var controller = CreateController(service.Object, notifier.Object);

        var result = await controller.DeleteReply(4, 9, 12);

        var response = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<DiscussionReplyResponse>>().Subject;
        response.Data!.IsDeleted.Should().BeTrue();
        response.Data.UserId.Should().BeNull();
        response.Data.Content.Should().BeNull();
        response.Data.Author.Should().BeNull();
        notifier.Verify(
            n => n.ReplyDeletedAsync(4, It.Is<DiscussionReplyResponse>(r => r.IsDeleted)),
            Times.Once);
    }

    [Fact]
    public async Task ReactionEndpoints_ShouldReturnViewerStateAndBroadcastAggregates()
    {
        var service = new Mock<IClubDiscussionReplyService>();
        var notifier = new Mock<IClubRealtimeNotifier>();
        service.Setup(s => s.SetReactionAsync(
                4, 9, 12, 7, "Participant", DiscussionReplyReactionType.Dislike))
            .ReturnsAsync(new DiscussionReplyReactionSummary(2, 3, DiscussionReplyReactionType.Dislike));
        service.Setup(s => s.ClearReactionAsync(4, 9, 12, 7, "Participant"))
            .ReturnsAsync(new DiscussionReplyReactionSummary(2, 2, null));
        var controller = CreateController(service.Object, notifier.Object);

        var setResult = await controller.SetReaction(4, 9, 12, new DiscussionReplyReactionRequest
        {
            Reaction = DiscussionReplyReactionType.Dislike
        });
        var setResponse = setResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<DiscussionReplyReactionResponse>>().Subject;
        setResponse.Data!.CurrentUserReaction.Should().Be("Dislike");
        setResponse.Data.DislikeCount.Should().Be(3);
        notifier.Verify(n => n.ReplyReactionChangedAsync(4, 9, 12, 2, 3), Times.Once);

        var clearResult = await controller.ClearReaction(4, 9, 12);
        clearResult.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<DiscussionReplyReactionResponse>>()
            .Which.Data!.CurrentUserReaction.Should().BeNull();
        notifier.Verify(n => n.ReplyReactionChangedAsync(4, 9, 12, 2, 2), Times.Once);
    }

    private static ClubDiscussionReplyController CreateController(
        IClubDiscussionReplyService service,
        IClubRealtimeNotifier notifier)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Name, "member@example.com"),
                new Claim(ClaimTypes.Role, "Participant")
            ], "TestAuth");

        return new ClubDiscussionReplyController(service, notifier)
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

    private static DiscussionReplyView CreateView(
        int? parentReplyId = null,
        string content = "Discussion reply",
        bool isDeleted = false)
    {
        var createdAt = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        return new DiscussionReplyView(
            new ClubDiscussionReply
            {
                Id = 12,
                DiscussionId = 9,
                ParentReplyId = parentReplyId,
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
            isDeleted ? null : DiscussionReplyReactionType.Like,
            4);
    }
}
