using backend.main.features.clubs.realtime;
using backend.main.features.clubs.realtime.contracts.responses;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs;

public class ClubPresenceStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static PresenceUser User(int id) =>
        new(id, $"User {id}", $"user{id}", null);

    [Fact]
    public void JoinClub_ShouldReportAUserOnlineOnlyOnTheirFirstConnection()
    {
        var store = new ClubPresenceStore();

        store.JoinClub(1, "conn-a", User(7)).Should().BeTrue();
        store.JoinClub(1, "conn-b", User(7)).Should().BeFalse("a second tab is the same person");

        var snapshot = store.Snapshot(1);
        snapshot.TotalOnline.Should().Be(1);
        snapshot.Users.Should().ContainSingle().Which.UserId.Should().Be(7);
    }

    [Fact]
    public void LeaveClub_ShouldKeepAUserOnlineWhileAnotherTabRemains()
    {
        var store = new ClubPresenceStore();
        store.JoinClub(1, "conn-a", User(7));
        store.JoinClub(1, "conn-b", User(7));

        store.LeaveClub(1, "conn-a", out var stillOnline).Should().BeFalse();
        stillOnline.Should().BeNull();
        store.Snapshot(1).TotalOnline.Should().Be(1);

        store.LeaveClub(1, "conn-b", out var wentOffline).Should().BeTrue();
        wentOffline!.UserId.Should().Be(7);
        store.Snapshot(1).TotalOnline.Should().Be(0);
    }

    [Fact]
    public void JoinClub_ShouldTrackAnonymousConnectionsWithoutListingThem()
    {
        var store = new ClubPresenceStore();

        store.JoinClub(1, "anon", user: null).Should().BeFalse();

        store.Snapshot(1).TotalOnline.Should().Be(0);
        store.Snapshot(1).Users.Should().BeEmpty();

        // Still tracked, so a disconnect can clean up its group membership.
        store.ClubsFor("anon").Should().Equal(1);
    }

    [Fact]
    public void Snapshot_ShouldCapTheRosterButNotTheCount()
    {
        var store = new ClubPresenceStore();
        var total = ClubPresenceStore.MaxRosterUsers + 10;
        for (var id = 1; id <= total; id++)
            store.JoinClub(1, $"conn-{id}", User(id));

        var snapshot = store.Snapshot(1);

        snapshot.Users.Should().HaveCount(ClubPresenceStore.MaxRosterUsers);
        snapshot.TotalOnline.Should().Be(total);
    }

    [Fact]
    public void ClubsFor_ShouldListEveryClubAConnectionJoined()
    {
        var store = new ClubPresenceStore();
        store.JoinClub(1, "conn", User(7));
        store.JoinClub(2, "conn", User(7));

        store.ClubsFor("conn").Should().BeEquivalentTo([1, 2]);
        store.ClubsFor("unknown").Should().BeEmpty();
    }

    [Fact]
    public void SetTyping_ShouldBroadcastOnlyWhenTheRosterActuallyChanges()
    {
        var store = new ClubPresenceStore();
        const string thread = "thread:discussion:9";

        store.SetTyping(thread, "conn-a", User(7), isTyping: true, Now).Should().BeTrue();

        // The client refreshes on a timer; a refresh must not re-broadcast.
        store.SetTyping(thread, "conn-a", User(7), isTyping: true, Now.AddSeconds(2)).Should().BeFalse();

        // Nor should the same person typing from a second tab.
        store.SetTyping(thread, "conn-b", User(7), isTyping: true, Now.AddSeconds(2)).Should().BeFalse();

        store.SetTyping(thread, "conn-c", User(8), isTyping: true, Now.AddSeconds(2)).Should().BeTrue();
        store.Typing(thread).Users.Should().HaveCount(2);
    }

    [Fact]
    public void SetTyping_ShouldClearAnEntryWhenTypingStops()
    {
        var store = new ClubPresenceStore();
        const string thread = "thread:post:3";
        store.SetTyping(thread, "conn-a", User(7), isTyping: true, Now);

        store.SetTyping(thread, "conn-a", User(7), isTyping: false, Now).Should().BeTrue();
        store.Typing(thread).Users.Should().BeEmpty();

        // Clearing something that was never set is not a change.
        store.SetTyping(thread, "conn-a", User(7), isTyping: false, Now).Should().BeFalse();
    }

    [Fact]
    public void ExpireTyping_ShouldDropStaleEntriesAndNameTheChangedThreads()
    {
        var store = new ClubPresenceStore();
        const string stale = "thread:discussion:1";
        const string fresh = "thread:discussion:2";
        store.SetTyping(stale, "conn-a", User(7), isTyping: true, Now);
        store.SetTyping(fresh, "conn-b", User(8), isTyping: true, Now);

        var justBeforeExpiry = Now + ClubPresenceStore.TypingTtl - TimeSpan.FromMilliseconds(1);
        store.ExpireTyping(justBeforeExpiry).Should().BeEmpty();

        // Refresh only the second thread, then step past the TTL.
        store.SetTyping(fresh, "conn-b", User(8), isTyping: true, justBeforeExpiry);
        var changed = store.ExpireTyping(Now + ClubPresenceStore.TypingTtl);

        changed.Should().Equal(stale);
        store.Typing(stale).Users.Should().BeEmpty();
        store.Typing(fresh).Users.Should().ContainSingle().Which.UserId.Should().Be(8);
    }

    [Fact]
    public void ExpireTyping_ShouldNotReportAChangeWhileAnotherTabOfTheSameUserIsStillTyping()
    {
        var store = new ClubPresenceStore();
        const string thread = "thread:post:5";
        store.SetTyping(thread, "conn-a", User(7), isTyping: true, Now);
        store.SetTyping(thread, "conn-b", User(7), isTyping: true, Now.AddSeconds(3));

        var changed = store.ExpireTyping(Now + ClubPresenceStore.TypingTtl);

        changed.Should().BeEmpty();
        store.Typing(thread).Users.Should().ContainSingle().Which.UserId.Should().Be(7);
    }

    [Fact]
    public void LeaveThread_ShouldClearTypingAndUnregisterTheConnection()
    {
        var store = new ClubPresenceStore();
        const string thread = "thread:discussion:9";
        store.JoinThread("conn-a", thread);
        store.SetTyping(thread, "conn-a", User(7), isTyping: true, Now);

        store.IsInThread("conn-a", thread).Should().BeTrue();
        store.ThreadsFor("conn-a").Should().Equal(thread);

        store.LeaveThread("conn-a", thread).Should().BeTrue();

        store.IsInThread("conn-a", thread).Should().BeFalse();
        store.ThreadsFor("conn-a").Should().BeEmpty();
        store.Typing(thread).Users.Should().BeEmpty();
    }

    [Fact]
    public void IsInThread_ShouldRejectAConnectionThatNeverJoined()
    {
        var store = new ClubPresenceStore();
        store.JoinThread("conn-a", "thread:discussion:9");

        store.IsInThread("conn-a", "thread:discussion:10").Should().BeFalse();
        store.IsInThread("conn-b", "thread:discussion:9").Should().BeFalse();
    }

    [Fact]
    public void JoinClub_ShouldRefreshCachedDisplayInfoAcrossTabs()
    {
        var store = new ClubPresenceStore();
        store.JoinClub(1, "conn-a", new PresenceUser(7, "Old Name", "old", null));

        store.JoinClub(1, "conn-b", new PresenceUser(7, "New Name", "new", "avatar.png"));

        var user = store.Snapshot(1).Users.Should().ContainSingle().Subject;
        user.Name.Should().Be("New Name");
        user.Avatar.Should().Be("avatar.png");
    }
}
