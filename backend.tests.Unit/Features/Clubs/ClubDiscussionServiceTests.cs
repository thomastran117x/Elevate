using backend.main.features.clubs.discussions;
using backend.main.features.clubs.moderation;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionServiceTests
{
    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenClubDoesNotExist()
    {
        var harness = new ClubDiscussionServiceHarness();

        var action = () => harness.Service.CreateAsync(harness.ClubId, harness.UserId, "Participant", "Topic", "Body");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage($"Club {harness.ClubId} not found");
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenUserIsNotAMember()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: false);

        var action = () => harness.Service.CreateAsync(harness.ClubId, harness.UserId, "Participant", "Topic", "Body");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to start a discussion.");
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistDiscussion_WhenUserIsAMember()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.CreateAsync(It.IsAny<ClubDiscussion>()))
            .ReturnsAsync((ClubDiscussion discussion) =>
            {
                discussion.Id = 42;
                return discussion;
            });

        var created = await harness.Service.CreateAsync(
            harness.ClubId, harness.UserId, "Participant", "Weekend ride", "Where should we go?");

        created.Id.Should().Be(42);
        created.ClubId.Should().Be(harness.ClubId);
        created.UserId.Should().Be(harness.UserId);
        created.Title.Should().Be("Weekend ride");
        created.Description.Should().Be("Where should we go?");
    }

    [Fact]
    public async Task CreateAsync_ShouldAllowStaff_WhenTheyHaveNotJoined()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: true);
        harness.SetupMembership(isStaff: true, isMember: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.CreateAsync(It.IsAny<ClubDiscussion>()))
            .ReturnsAsync((ClubDiscussion discussion) => discussion);

        var created = await harness.Service.CreateAsync(
            harness.ClubId, harness.UserId, "Organizer", "Staff topic", "Body");

        created.Title.Should().Be("Staff topic");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldReturnDiscussionsAndAuthors_ForAPublicClubAnonymously()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);

        var discussions = new List<ClubDiscussion>
        {
            new() { Id = 2, ClubId = harness.ClubId, UserId = harness.UserId, Title = "Newer", Description = "B" },
            new() { Id = 1, ClubId = harness.ClubId, UserId = harness.UserId, Title = "Older", Description = "A" }
        };

        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByClubIdAsync(harness.ClubId, 1, 20))
            .ReturnsAsync(discussions);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.CountByClubIdAsync(harness.ClubId))
            .ReturnsAsync(2);
        harness.UserRepositoryMock
            .Setup(repo => repo.GetByIdsAsync(It.Is<List<int>>(ids => ids.Count == 1 && ids[0] == harness.UserId)))
            .ReturnsAsync([new UserListRecord { Id = harness.UserId, Email = "a@test.local", Username = "rider", Name = "Rider", Usertype = "Participant" }]);

        var (items, authors, totalCount) =
            await harness.Service.GetByClubIdAsync(harness.ClubId, null, null, 1, 20);

        items.Should().BeSameAs(discussions);
        totalCount.Should().Be(2);
        authors.Should().ContainKey(harness.UserId);
        authors[harness.UserId].Name.Should().Be("Rider");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldSkipAuthorLookup_WhenThereAreNoDiscussions()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByClubIdAsync(harness.ClubId, 1, 20))
            .ReturnsAsync([]);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.CountByClubIdAsync(harness.ClubId))
            .ReturnsAsync(0);

        var (items, authors, totalCount) =
            await harness.Service.GetByClubIdAsync(harness.ClubId, null, null, 1, 20);

        items.Should().BeEmpty();
        authors.Should().BeEmpty();
        totalCount.Should().Be(0);
        harness.UserRepositoryMock.Verify(repo => repo.GetByIdsAsync(It.IsAny<List<int>>()), Times.Never);
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldThrowUnauthorized_ForAPrivateClubAnonymously()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: true);

        var action = () => harness.Service.GetByClubIdAsync(harness.ClubId, null, null, 1, 20);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Authentication is required to view discussions for a private club.");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldThrowForbidden_ForAPrivateClubWhenNotAMember()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: true);
        harness.SetupMembership(isStaff: false, isMember: false);

        var action = () => harness.Service.GetByClubIdAsync(harness.ClubId, harness.UserId, "Participant", 1, 20);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to view its discussions.");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldSucceed_ForAPrivateClubWhenAMember()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: true);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByClubIdAsync(harness.ClubId, 1, 20))
            .ReturnsAsync([]);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.CountByClubIdAsync(harness.ClubId))
            .ReturnsAsync(0);

        var (_, _, totalCount) =
            await harness.Service.GetByClubIdAsync(harness.ClubId, harness.UserId, "Participant", 1, 20);

        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenDiscussionDoesNotExist()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(50))
            .ReturnsAsync((ClubDiscussion?)null);

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 50, harness.UserId, "Participant", "T", "D");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Discussion with ID 50 was not found.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenDiscussionBelongsToAnotherClub()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(51))
            .ReturnsAsync(new ClubDiscussion { Id = 51, ClubId = 999, UserId = harness.UserId });

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 51, harness.UserId, "Participant", "T", "D");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Discussion with ID 51 was not found.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrow_WhenDiscussionBelongsToAnotherUser()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(52))
            .ReturnsAsync(new ClubDiscussion { Id = 52, ClubId = harness.ClubId, UserId = harness.OtherUserId });

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 52, harness.UserId, "Participant", "T", "D");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to update this discussion.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotLetStaffRewriteSomeoneElsesDiscussion()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: true, isMember: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(55))
            .ReturnsAsync(new ClubDiscussion { Id = 55, ClubId = harness.ClubId, UserId = harness.MemberAuthorId });

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 55, harness.UserId, "Volunteer", "T", "D");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to update this discussion.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldRefuseAnAuthorWhoNoLongerBelongsToTheClub()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: true);

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 56, harness.UserId, "Participant", "T", "D");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage(ClubDiscussionService.ContributeForbiddenMessage);
        harness.DiscussionRepositoryMock.Verify(repo => repo.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenTheRowDisappearsMidUpdate()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(53))
            .ReturnsAsync(new ClubDiscussion { Id = 53, ClubId = harness.ClubId, UserId = harness.UserId });
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.UpdateAsync(53, It.IsAny<ClubDiscussion>()))
            .ReturnsAsync((ClubDiscussion?)null);

        var action = () => harness.Service.UpdateAsync(harness.ClubId, 53, harness.UserId, "Participant", "T", "D");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Discussion with ID 53 was not found.");
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateDiscussion_WhenCallerIsTheAuthor()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(54))
            .ReturnsAsync(new ClubDiscussion
            {
                Id = 54,
                ClubId = harness.ClubId,
                UserId = harness.UserId,
                Title = "Old",
                Description = "Old body"
            });
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.UpdateAsync(54, It.IsAny<ClubDiscussion>()))
            .ReturnsAsync((int _, ClubDiscussion updated) =>
            {
                updated.Id = 54;
                updated.ClubId = harness.ClubId;
                updated.UserId = harness.UserId;
                return updated;
            });

        var updated = await harness.Service.UpdateAsync(harness.ClubId, 54, harness.UserId, "Participant", "New", "New body");

        updated.Title.Should().Be("New");
        updated.Description.Should().Be("New body");
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrow_WhenDiscussionDoesNotExist()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(60))
            .ReturnsAsync((ClubDiscussion?)null);

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 60, harness.UserId, "Participant");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Discussion with ID 60 was not found.");
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowNotFound_WhenDiscussionBelongsToAnotherClub()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(61))
            .ReturnsAsync(new ClubDiscussion { Id = 61, ClubId = 999, UserId = harness.UserId });

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 61, harness.UserId, "Participant");

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Discussion with ID 61 was not found.");
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrow_WhenAMemberDeletesSomeoneElsesDiscussion()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(62))
            .ReturnsAsync(new ClubDiscussion { Id = 62, ClubId = harness.ClubId, UserId = harness.MemberAuthorId });

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 62, harness.UserId, "Participant", "Spam");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You are not allowed to delete this discussion.");
        harness.DiscussionRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRefuseAVolunteerRemovingTheOwnersDiscussion()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: true, isMember: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(64))
            .ReturnsAsync(new ClubDiscussion { Id = 64, ClubId = harness.ClubId, UserId = harness.OtherUserId });

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 64, harness.UserId, "Volunteer", "Spam");

        await action.Should().ThrowAsync<ForbiddenException>();
        harness.ModerationLogMock.Verify(log => log.Stage(It.IsAny<ClubModerationAction>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRefuseAnAuthorWhoNoLongerBelongsToTheClub()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 65, harness.UserId, "Participant");

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage(ClubDiscussionService.ContributeForbiddenMessage);
        harness.DiscussionRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteDiscussion_WhenCallerIsTheAuthor_WithoutLogging()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: false, isMember: true);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(63))
            .ReturnsAsync(new ClubDiscussion { Id = 63, ClubId = harness.ClubId, UserId = harness.UserId });
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.DeleteAsync(63))
            .ReturnsAsync(true);

        await harness.Service.DeleteAsync(harness.ClubId, 63, harness.UserId, "Participant");

        harness.DiscussionRepositoryMock.Verify(repo => repo.DeleteAsync(63), Times.Once);
        harness.ModerationLogMock.Verify(log => log.Stage(It.IsAny<ClubModerationAction>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRequireAReason_WhenStaffRemoveSomeoneElsesDiscussion()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: true, isMember: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(66))
            .ReturnsAsync(new ClubDiscussion { Id = 66, ClubId = harness.ClubId, UserId = harness.MemberAuthorId });

        var action = () => harness.Service.DeleteAsync(harness.ClubId, 66, harness.UserId, "Volunteer", " ");

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage(ClubModeration.ReasonRequiredMessage);
        harness.DiscussionRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldLogTheRemoval_WhenStaffRemoveAMembersDiscussion()
    {
        var harness = new ClubDiscussionServiceHarness();
        harness.SetupClub(isPrivate: false);
        harness.SetupMembership(isStaff: true, isMember: false);
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.GetByIdAsync(67))
            .ReturnsAsync(new ClubDiscussion
            {
                Id = 67,
                ClubId = harness.ClubId,
                UserId = harness.MemberAuthorId,
                Title = "Cheap watches"
            });
        harness.DiscussionRepositoryMock
            .Setup(repo => repo.DeleteAsync(67))
            .ReturnsAsync(true);
        ClubModerationAction? staged = null;
        harness.ModerationLogMock
            .Setup(log => log.Stage(It.IsAny<ClubModerationAction>()))
            .Callback((ClubModerationAction entry) => staged = entry);

        await harness.Service.DeleteAsync(harness.ClubId, 67, harness.UserId, "Volunteer", "Spam");

        harness.DiscussionRepositoryMock.Verify(repo => repo.DeleteAsync(67), Times.Once);
        staged.Should().BeEquivalentTo(new
        {
            ClubId = harness.ClubId,
            ActorUserId = harness.UserId,
            ActorClubRole = ClubModerationRoles.Volunteer,
            Action = ClubModerationActionTypes.Remove,
            ContentType = ClubModerationContentTypes.Discussion,
            ContentId = 67,
            AuthorUserId = harness.MemberAuthorId,
            ContentSnapshot = "Cheap watches",
            Reason = "Spam",
        });
    }

    private sealed class ClubDiscussionServiceHarness
    {
        public ClubDiscussionService Service { get; }
        public Mock<IClubDiscussionRepository> DiscussionRepositoryMock { get; } = new();
        public FakeClubAuthorizer Access { get; } = new();
        public Mock<IUserRepository> UserRepositoryMock { get; } = new();
        public Mock<IClubModerationLog> ModerationLogMock { get; } = new();

        public int ClubId => 7;
        public int UserId => 11;

        /// <summary>The club's owner.</summary>
        public int OtherUserId => 12;

        /// <summary>A member with no staff role.</summary>
        public int MemberAuthorId => 13;

        public ClubDiscussionServiceHarness()
        {
            Service = new ClubDiscussionService(
                DiscussionRepositoryMock.Object,
                Access,
                UserRepositoryMock.Object,
                ModerationLogMock.Object);
            Access.Member(ClubId, MemberAuthorId);
        }

        public void SetupClub(bool isPrivate) => Access.Club(ClubId, isPrivate, ownerId: OtherUserId);

        public void SetupMembership(bool isStaff, bool isMember)
        {
            if (isStaff)
                Access.Volunteer(ClubId, UserId);
            if (isMember)
                Access.Member(ClubId, UserId);
        }
    }
}
