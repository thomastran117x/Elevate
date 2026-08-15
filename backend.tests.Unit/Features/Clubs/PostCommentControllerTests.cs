using System.Reflection;

using backend.main.features.clubs.posts.comments;

using FluentAssertions;

using Microsoft.AspNetCore.Http.Timeouts;

namespace backend.tests.Unit.Features.Clubs;

public class PostCommentControllerTests
{
    [Fact]
    public void StreamComments_ShouldDisableTheGlobalRequestTimeout()
    {
        var action = typeof(PostCommentController)
            .GetMethod(nameof(PostCommentController.StreamComments));

        action.Should().NotBeNull();
        action!.GetCustomAttribute<DisableRequestTimeoutAttribute>().Should().NotBeNull();
    }
}
