using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.features.clubs.realtime;

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubRealtimeNotifierTests
{
    [Fact]
    public async Task Broadcast_ShouldNotWaitOnABackPressuredSubscriber()
    {
        var blocked = new TaskCompletionSource();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();

        // Stands in for a subscriber whose transport buffer is full: the send never completes.
        proxy
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(blocked.Task);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);

        var hub = new Mock<IHubContext<ClubRealtimeHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var notifier = new ClubRealtimeNotifier(hub.Object);

        // Awaiting the fan-out inside an HTTP handler would hang here; it must return at once.
        var broadcast = notifier.ReplyCreatedAsync(4, new DiscussionReplyResponse { Id = 1 });

        await broadcast.WaitAsync(TimeSpan.FromSeconds(5));
        broadcast.IsCompletedSuccessfully.Should().BeTrue();

        blocked.SetResult();
    }

    [Fact]
    public async Task Broadcast_ShouldTargetTheClubAndPostGroups()
    {
        var groups = new List<string>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        proxy
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        clients
            .Setup(c => c.Group(It.IsAny<string>()))
            .Callback<string>(groups.Add)
            .Returns(proxy.Object);

        var hub = new Mock<IHubContext<ClubRealtimeHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var notifier = new ClubRealtimeNotifier(hub.Object);

        await notifier.ReplyCreatedAsync(4, new DiscussionReplyResponse { Id = 1 });
        await notifier.CommentReactionChangedAsync(4, 8, 12, 1, 0);

        groups.Should().Equal(
            ClubRealtimeGroups.Club(4),
            ClubRealtimeGroups.Post(4, 8));
    }
}
