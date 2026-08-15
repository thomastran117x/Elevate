using System.Reflection;

using backend.main.features.clubs.discussions.replies;

using FluentAssertions;

using Microsoft.AspNetCore.Http.Timeouts;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionReplyControllerTests
{
    [Fact]
    public void StreamReplyEvents_ShouldDisableTheGlobalRequestTimeout()
    {
        var action = typeof(ClubDiscussionReplyController)
            .GetMethod(nameof(ClubDiscussionReplyController.StreamReplyEvents));

        action.Should().NotBeNull();
        action!.GetCustomAttribute<DisableRequestTimeoutAttribute>().Should().NotBeNull();
    }
}
