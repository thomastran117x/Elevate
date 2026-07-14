using System.Reflection;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.contracts;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.clubs.versions;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubServiceTests
{
    [Fact]
    public async Task CreateClub_ShouldPersistVersion_StageUpsert_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.UserServiceMock
            .Setup(service => service.GetUserByIdAsync(harness.OwnerUserId))
            .ReturnsAsync(new User
            {
                Id = harness.OwnerUserId,
                Email = "owner@test.local",
                Usertype = " Organizer "
            });

        var created = await harness.Service.CreateClub(
            "Chess Club",
            harness.OwnerUserId,
            "A focused club for competitive and casual chess players.",
            " gaming ",
            "https://cdn.test/clubs/new-club.png",
            phone: "555-0100",
            email: "chess@test.local");

        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("Chess Club");
        created.Clubtype.Should().Be(ClubType.Gaming);
        created.ClubImage.Should().Be("https://cdn.test/clubs/new-club.png");
        created.CurrentVersionNumber.Should().Be(1);

        var persisted = await harness.Db.Clubs.SingleAsync(club => club.Id == created.Id);
        persisted.Email.Should().Be("chess@test.local");
        persisted.Phone.Should().Be("555-0100");

        var version = await harness.Db.ClubVersions.SingleAsync(item => item.ClubId == created.Id);
        version.ActionType.Should().Be(ClubVersionActions.Create);
        version.ActorRole.Should().Be("Organizer");

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(club => club.Id == created.Id)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{created.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == created.Id && dto.Name == "Chess Club"),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    [Fact]
    public async Task GetClubAccessMapAsync_ShouldMarkOwnerManagerVolunteerAndNoAccess()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var owned = await harness.SeedPersistedClubAsync(id: 11, userId: harness.OwnerUserId);
        var managed = await harness.SeedPersistedClubAsync(id: 12, userId: harness.OtherOwnerUserId);
        var volunteered = await harness.SeedPersistedClubAsync(id: 13, userId: harness.OtherOwnerUserId);
        var inaccessible = await harness.SeedPersistedClubAsync(id: 14, userId: harness.OtherOwnerUserId);

        harness.Db.ClubStaff.AddRange(
            new ClubStaff
            {
                ClubId = managed.Id,
                UserId = harness.OwnerUserId,
                Role = ClubStaffRole.Manager,
                GrantedByUserId = harness.OtherOwnerUserId
            },
            new ClubStaff
            {
                ClubId = volunteered.Id,
                UserId = harness.OwnerUserId,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = harness.OtherOwnerUserId
            });
        await harness.Db.SaveChangesAsync();

        var access = await harness.Service.GetClubAccessMapAsync(
            [owned.Id, managed.Id, volunteered.Id, inaccessible.Id],
            harness.OwnerUserId,
            harness.OwnerRole);

        access[owned.Id].IsOwner.Should().BeTrue();
        access[owned.Id].CanManage.Should().BeTrue();
        access[managed.Id].IsManager.Should().BeTrue();
        access[managed.Id].CanManage.Should().BeTrue();
        access[volunteered.Id].IsVolunteer.Should().BeTrue();
        access[volunteered.Id].CanManage.Should().BeFalse();
        access[inaccessible.Id].CanManage.Should().BeFalse();
    }

    [Fact]
    public async Task GetClubAccessMapAsync_ShouldGrantManageAccess_ForAdmin()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var first = await harness.SeedPersistedClubAsync(id: 21, userId: harness.OtherOwnerUserId);
        var second = await harness.SeedPersistedClubAsync(id: 22, userId: harness.OtherOwnerUserId);

        var access = await harness.Service.GetClubAccessMapAsync(
            [first.Id, second.Id],
            harness.MemberUserId,
            "Admin");

        access[first.Id].CanManage.Should().BeTrue();
        access[second.Id].CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllClubs_ShouldReturnElasticsearchResultsInHitOrder_AndNormalizeQuery()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        ClubSearchCriteria? capturedCriteria = null;
        var second = harness.BuildClub(id: 32, name: "Second Club");
        var first = harness.BuildClub(id: 31, name: "First Club");

        harness.CacheMock
            .Setup(cache => cache.GetValueAsync("clubs:version"))
            .ReturnsAsync("7");
        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.Is<string>(key => key.StartsWith("clubs:list:"))))
            .ReturnsAsync((string?)null);
        harness.CacheMock
            .Setup(cache => cache.GetManyAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string?>());
        harness.SearchServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<ClubSearchCriteria>()))
            .Callback<ClubSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(new ClubSearchResult(
                [
                    new ClubSearchHit(second.Id),
                    new ClubSearchHit(first.Id)
                ],
                2));
        harness.ClubRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([first, second]);

        var result = await harness.Service.GetAllClubs(new ClubSearchCriteria
        {
            Query = "  Board Games  ",
            ClubType = ClubType.Gaming,
            Page = 2,
            PageSize = 15
        });

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Query.Should().Be("board games");
        result.Clubs.Select(club => club.Id).Should().Equal(second.Id, first.Id);
        result.TotalCount.Should().Be(2);
        result.Source.Should().Be(ResponseSource.Elasticsearch);
        harness.CacheMock.Verify(cache => cache.SetValueAsync(
            It.Is<string>(key => key.Contains("v7") && key.Contains("board games")),
            It.IsAny<string>(),
            It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllClubs_ShouldFallbackToDatabase_WhenElasticsearchIsUnavailable()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        ClubSearchCriteria? capturedCriteria = null;
        var expected = harness.BuildClub(id: 41, name: "Fallback Club");

        harness.CacheMock
            .Setup(cache => cache.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        harness.SearchServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<ClubSearchCriteria>()))
            .ThrowsAsync(new ElasticsearchUnavailableException("search offline"));
        harness.ClubRepositoryMock
            .Setup(repository => repository.SearchAsync(It.IsAny<ClubSearchCriteria>()))
            .Callback<ClubSearchCriteria>(criteria => capturedCriteria = criteria)
            .ReturnsAsync(([expected], 1));

        var result = await harness.Service.GetAllClubs(new ClubSearchCriteria
        {
            Query = "  Robotics  "
        });

        capturedCriteria.Should().NotBeNull();
        capturedCriteria!.Query.Should().Be("robotics");
        result.Clubs.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
        result.Source.Should().Be(ResponseSource.Database);
        harness.CacheMock.Verify(cache => cache.SetValueAsync("clubs:version", "1", null), Times.Once);
    }

    [Fact]
    public async Task GetClubsByIdsAsync_ShouldReturnCachedAndFetchedClubs()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var cachedClub = harness.BuildClub(id: 51, name: "Cached Club");
        var fetchedClub = harness.BuildClub(id: 52, name: "Fetched Club");

        harness.CacheMock
            .Setup(cache => cache.GetManyAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string?>
            {
                ["club:51"] = JsonSerializer.Serialize(ClubCacheMapper.ToDto(cachedClub))
            });
        harness.ClubRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([fetchedClub]);

        var result = await harness.Service.GetClubsByIdsAsync([cachedClub.Id, fetchedClub.Id]);

        result.Select(club => club.Id).Should().Equal(cachedClub.Id, fetchedClub.Id);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            "club:52",
            It.Is<ClubCacheDto>(dto => dto.Id == fetchedClub.Id),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateClub_ShouldPersistVersion_StageUpsert_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var existing = await harness.SeedPersistedClubAsync(id: 61, userId: harness.OwnerUserId);

        var updated = await harness.Service.UpdateClub(
            existing.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            "Updated Club",
            "An updated description for the club.",
            "music",
            "https://cdn.test/clubs/updated.png",
            phone: "555-0111",
            email: "updated@test.local");

        updated.Name.Should().Be("Updated Club");
        updated.Clubtype.Should().Be(ClubType.Cultural);
        updated.ClubImage.Should().Be("https://cdn.test/clubs/updated.png");
        updated.CurrentVersionNumber.Should().Be(2);

        var persisted = await harness.Db.Clubs.SingleAsync(club => club.Id == existing.Id);
        persisted.Email.Should().Be("updated@test.local");
        persisted.CurrentVersionNumber.Should().Be(2);

        var latestVersion = await harness.Db.ClubVersions
            .Where(item => item.ClubId == existing.Id)
            .OrderByDescending(item => item.VersionNumber)
            .FirstAsync();
        latestVersion.ActionType.Should().Be(ClubVersionActions.Update);

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(club => club.Id == existing.Id && club.CurrentVersionNumber == 2)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{existing.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == existing.Id && dto.Name == "Updated Club"),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    [Fact]
    public async Task DeleteClub_ShouldDeleteImage_StageDelete_AndClearCache()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var existing = await harness.SeedPersistedClubAsync(id: 71, userId: harness.OwnerUserId);

        await harness.Service.DeleteClub(existing.Id, harness.OwnerUserId);

        (await harness.Db.Clubs.FindAsync(existing.Id)).Should().BeNull();
        harness.BlobServiceMock.Verify(service => service.DeleteBlobAsync(existing.ClubImage), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageDelete(existing.Id), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"club:{existing.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    [Fact]
    public async Task DeleteClub_ShouldRejectNonOwner()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var existing = await harness.SeedPersistedClubAsync(id: 72, userId: harness.OwnerUserId);

        var action = () => harness.Service.DeleteClub(existing.Id, harness.MemberUserId);

        await action.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task CreateClub_ShouldRejectUnownedBlobUrls()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        harness.UserServiceMock
            .Setup(service => service.GetUserByIdAsync(harness.OwnerUserId))
            .ReturnsAsync(new User
            {
                Id = harness.OwnerUserId,
                Email = "owner@test.local",
                Usertype = "Organizer"
            });

        var act = () => harness.Service.CreateClub(
            "Chess Club",
            harness.OwnerUserId,
            "A focused club for competitive and casual chess players.",
            "gaming",
            "https://example.com/clubs/chess.png");

        await act.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Club images must reference uploads issued by this service.");
    }

    [Fact]
    public async Task CreateClub_ShouldRejectNonHttpsClubImageUrls()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        harness.UserServiceMock
            .Setup(service => service.GetUserByIdAsync(harness.OwnerUserId))
            .ReturnsAsync(new User
            {
                Id = harness.OwnerUserId,
                Email = "owner@test.local",
                Usertype = "Organizer"
            });

        var act = () => harness.Service.CreateClub(
            "Chess Club",
            harness.OwnerUserId,
            "A focused club for competitive and casual chess players.",
            "gaming",
            "http://cdn.test/clubs/chess.png");

        await act.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Club images must use a valid HTTPS URL.");
    }

    [Fact]
    public async Task JoinClubAsync_ShouldIncrementMembers_StageUpsert_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.ConfigureClubPersistence();
        var club = await harness.SeedPersistedClubAsync(id: 81, userId: harness.OtherOwnerUserId, memberCount: 4);

        harness.FollowServiceMock
            .Setup(service => service.IsMemberAsync(club.Id, harness.MemberUserId))
            .ReturnsAsync(false);

        await harness.Service.JoinClubAsync(club.Id, harness.MemberUserId);

        var persisted = await harness.Db.Clubs.SingleAsync(item => item.Id == club.Id);
        persisted.MemberCount.Should().Be(5);
        harness.FollowServiceMock.Verify(service => service.AddMembershipAsync(club.Id, harness.MemberUserId), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(entity => entity.Id == club.Id && entity.MemberCount == 5)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{club.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == club.Id && dto.MemberCount == 5),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task LeaveClubAsync_ShouldDecrementMembers_StageUpsert_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.ConfigureClubPersistence();
        var club = await harness.SeedPersistedClubAsync(id: 91, userId: harness.OtherOwnerUserId, memberCount: 3);

        harness.FollowServiceMock
            .Setup(service => service.IsMemberAsync(club.Id, harness.MemberUserId))
            .ReturnsAsync(true);

        await harness.Service.LeaveClubAsync(club.Id, harness.MemberUserId);

        var persisted = await harness.Db.Clubs.SingleAsync(item => item.Id == club.Id);
        persisted.MemberCount.Should().Be(2);
        harness.FollowServiceMock.Verify(service => service.RemoveMembershipAsync(club.Id, harness.MemberUserId), Times.Once);
        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(entity => entity.Id == club.Id && entity.MemberCount == 2)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{club.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == club.Id && dto.MemberCount == 2),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task JoinClubAsync_PrivateClub_ThrowsForbiddenAndDoesNotGrant()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.ConfigureClubPersistence();
        var club = await harness.SeedPersistedClubAsync(id: 101, userId: harness.OtherOwnerUserId, memberCount: 2);
        club.isPrivate = true;
        await harness.Db.SaveChangesAsync();

        var act = () => harness.Service.JoinClubAsync(club.Id, harness.MemberUserId);

        await act.Should().ThrowAsync<ForbiddenException>();
        harness.FollowServiceMock.Verify(
            service => service.AddMembershipAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantMembershipFromInvitationAsync_PrivateClub_BypassesGateAndIncrementsMembers()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.ConfigureClubPersistence();
        var club = await harness.SeedPersistedClubAsync(id: 102, userId: harness.OtherOwnerUserId, memberCount: 4);
        club.isPrivate = true;
        await harness.Db.SaveChangesAsync();

        harness.FollowServiceMock
            .Setup(service => service.IsMemberAsync(club.Id, harness.MemberUserId))
            .ReturnsAsync(false);

        await harness.Service.GrantMembershipFromInvitationAsync(club.Id, harness.MemberUserId);

        var persisted = await harness.Db.Clubs.SingleAsync(item => item.Id == club.Id);
        persisted.MemberCount.Should().Be(5);
        harness.FollowServiceMock.Verify(service => service.AddMembershipAsync(club.Id, harness.MemberUserId), Times.Once);
    }

    [Fact]
    public async Task GrantMembershipFromInvitationAsync_AlreadyMember_IsNoOp()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.ConfigureClubPersistence();
        var club = await harness.SeedPersistedClubAsync(id: 103, userId: harness.OtherOwnerUserId, memberCount: 6);

        harness.FollowServiceMock
            .Setup(service => service.IsMemberAsync(club.Id, harness.MemberUserId))
            .ReturnsAsync(true);

        await harness.Service.GrantMembershipFromInvitationAsync(club.Id, harness.MemberUserId);

        var persisted = await harness.Db.Clubs.SingleAsync(item => item.Id == club.Id);
        persisted.MemberCount.Should().Be(6);
        harness.FollowServiceMock.Verify(
            service => service.AddMembershipAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task GetManagedClubsAsync_ShouldReturnOwnedAndStaffManagedClubs()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var owned = await harness.SeedPersistedClubAsync(id: 101, userId: harness.OwnerUserId);
        var managed = await harness.SeedPersistedClubAsync(id: 102, userId: harness.OtherOwnerUserId);
        await harness.SeedPersistedClubAsync(id: 103, userId: harness.OtherOwnerUserId);

        harness.Db.ClubStaff.Add(new ClubStaff
        {
            ClubId = managed.Id,
            UserId = harness.OwnerUserId,
            Role = ClubStaffRole.Manager,
            GrantedByUserId = harness.OtherOwnerUserId
        });
        await harness.Db.SaveChangesAsync();

        var clubs = await harness.Service.GetManagedClubsAsync(harness.OwnerUserId);

        clubs.Select(club => club.Id).Should().Equal(managed.Id, owned.Id);
    }

    [Fact]
    public async Task CanManageClubAsync_AndHasClubStaffAccessAsync_ShouldRespectRoles()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 111, userId: harness.OtherOwnerUserId);

        harness.Db.ClubStaff.AddRange(
            new ClubStaff
            {
                ClubId = club.Id,
                UserId = harness.OwnerUserId,
                Role = ClubStaffRole.Manager,
                GrantedByUserId = harness.OtherOwnerUserId
            },
            new ClubStaff
            {
                ClubId = club.Id,
                UserId = harness.MemberUserId,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = harness.OtherOwnerUserId
            });
        await harness.Db.SaveChangesAsync();

        (await harness.Service.CanManageClubAsync(club.Id, harness.OwnerUserId)).Should().BeTrue();
        (await harness.Service.HasClubStaffAccessAsync(club.Id, harness.MemberUserId)).Should().BeTrue();
        (await harness.Service.CanManageClubAsync(club.Id, harness.MemberUserId)).Should().BeFalse();
    }

    [Fact]
    public async Task GetStaffAsync_ShouldReturnAssignmentsOrderedByCreatedAt()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 121, userId: harness.OwnerUserId);

        harness.Db.ClubStaff.AddRange(
            new ClubStaff
            {
                ClubId = club.Id,
                UserId = harness.OtherOwnerUserId,
                Role = ClubStaffRole.Manager,
                GrantedByUserId = harness.OwnerUserId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new ClubStaff
            {
                ClubId = club.Id,
                UserId = harness.MemberUserId,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = harness.OwnerUserId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            });
        await harness.Db.SaveChangesAsync();

        var staff = await harness.Service.GetStaffAsync(club.Id, harness.OwnerUserId, harness.OwnerRole);

        staff.Select(item => item.UserId).Should().Equal(harness.OtherOwnerUserId, harness.MemberUserId);
    }

    [Fact]
    public async Task AddStaffAsync_ShouldCreateAssignment_ForOwner()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 131, userId: harness.OwnerUserId);
        harness.UserServiceMock
            .Setup(service => service.GetUserByIdAsync(harness.MemberUserId))
            .ReturnsAsync(new User
            {
                Id = harness.MemberUserId,
                Email = "member@test.local",
                Usertype = "Participant"
            });

        var staff = await harness.Service.AddStaffAsync(
            club.Id,
            harness.MemberUserId,
            ClubStaffRole.Manager,
            harness.OwnerUserId,
            harness.OwnerRole);

        staff.ClubId.Should().Be(club.Id);
        staff.UserId.Should().Be(harness.MemberUserId);
        staff.Role.Should().Be(ClubStaffRole.Manager);

        var persisted = await harness.Db.ClubStaff.SingleAsync(item => item.ClubId == club.Id && item.UserId == harness.MemberUserId);
        persisted.Role.Should().Be(ClubStaffRole.Manager);
    }

    [Fact]
    public async Task RemoveStaffAsync_ShouldDeleteAssignment()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 141, userId: harness.OwnerUserId);
        harness.Db.ClubStaff.Add(new ClubStaff
        {
            ClubId = club.Id,
            UserId = harness.MemberUserId,
            Role = ClubStaffRole.Volunteer,
            GrantedByUserId = harness.OwnerUserId
        });
        await harness.Db.SaveChangesAsync();

        await harness.Service.RemoveStaffAsync(
            club.Id,
            harness.MemberUserId,
            harness.OwnerUserId,
            harness.OwnerRole);

        (await harness.Db.ClubStaff.FirstOrDefaultAsync(item => item.ClubId == club.Id && item.UserId == harness.MemberUserId))
            .Should().BeNull();
    }

    [Fact]
    public async Task TransferOwnershipAsync_ShouldMoveOwner_RemoveExistingStaffRole_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 151, userId: harness.OwnerUserId);
        harness.Db.ClubStaff.Add(new ClubStaff
        {
            ClubId = club.Id,
            UserId = harness.MemberUserId,
            Role = ClubStaffRole.Manager,
            GrantedByUserId = harness.OwnerUserId
        });
        await harness.Db.SaveChangesAsync();

        harness.UserServiceMock
            .Setup(service => service.GetUserByIdAsync(harness.MemberUserId))
            .ReturnsAsync(new User
            {
                Id = harness.MemberUserId,
                Email = "member@test.local",
                Usertype = "Participant"
            });

        var transferred = await harness.Service.TransferOwnershipAsync(
            club.Id,
            harness.MemberUserId,
            harness.OwnerUserId,
            harness.OwnerRole);

        transferred.UserId.Should().Be(harness.MemberUserId);
        var persisted = await harness.Db.Clubs.SingleAsync(item => item.Id == club.Id);
        persisted.UserId.Should().Be(harness.MemberUserId);
        (await harness.Db.ClubStaff.FirstOrDefaultAsync(item => item.ClubId == club.Id && item.UserId == harness.MemberUserId))
            .Should().BeNull();

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(entity => entity.Id == club.Id && entity.UserId == harness.MemberUserId)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{club.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == club.Id && dto.UserId == harness.MemberUserId),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync("clubs:version", 1), Times.Once);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ShouldReturnDescendingVersions_AndNormalizePaging()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 161, userId: harness.OwnerUserId);
        club.CurrentVersionNumber = 3;
        await harness.Db.SaveChangesAsync();

        harness.Db.ClubVersions.AddRange(
            new ClubVersion
            {
                ClubId = club.Id,
                VersionNumber = 1,
                ActionType = ClubVersionActions.Create,
                SnapshotJson = JsonSerializer.Serialize(new ClubVersionSnapshot
                {
                    Name = "Original Club",
                    Description = "Original description",
                    Clubtype = ClubType.Gaming.ToString(),
                    ClubImage = club.ClubImage,
                    Phone = club.Phone,
                    Email = club.Email,
                    WebsiteUrl = club.WebsiteUrl,
                    Location = club.Location,
                    MaxMemberCount = club.MaxMemberCount,
                    IsPrivate = club.isPrivate
                }),
                ChangedFieldsJson = "[]",
                ActorUserId = harness.OwnerUserId,
                ActorRole = harness.OwnerRole,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new ClubVersion
            {
                ClubId = club.Id,
                VersionNumber = 3,
                ActionType = ClubVersionActions.Update,
                SnapshotJson = JsonSerializer.Serialize(new ClubVersionSnapshot
                {
                    Name = "Current Club",
                    Description = club.Description,
                    Clubtype = club.Clubtype.ToString(),
                    ClubImage = club.ClubImage,
                    Phone = club.Phone,
                    Email = club.Email,
                    WebsiteUrl = club.WebsiteUrl,
                    Location = club.Location,
                    MaxMemberCount = club.MaxMemberCount,
                    IsPrivate = club.isPrivate
                }),
                ChangedFieldsJson = "[]",
                ActorUserId = harness.OwnerUserId,
                ActorRole = harness.OwnerRole,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await harness.Db.SaveChangesAsync();

        var history = await harness.Service.GetVersionHistoryAsync(
            club.Id,
            harness.OwnerUserId,
            harness.OwnerRole,
            page: 0,
            pageSize: 500);

        history.TotalCount.Should().Be(2);
        history.Items.Select(item => item.VersionNumber).Should().Equal(3, 1);
        history.Items.First().RollbackEligible.Should().BeFalse();
        history.Items.Last().RollbackEligible.Should().BeTrue();
    }

    [Fact]
    public async Task GetVersionDetailAsync_ShouldReturnSnapshotAndChangedFields()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 171, userId: harness.OwnerUserId);

        harness.Db.ClubVersions.Add(new ClubVersion
        {
            ClubId = club.Id,
            VersionNumber = 2,
            ActionType = ClubVersionActions.Update,
            SnapshotJson = JsonSerializer.Serialize(new ClubVersionSnapshot
            {
                Name = "Versioned Club",
                Description = "Versioned description",
                Clubtype = ClubType.Cultural.ToString(),
                ClubImage = "https://cdn.test/clubs/versioned.png",
                Phone = "555-2222",
                Email = "version@test.local",
                WebsiteUrl = "https://club.test",
                Location = "Campus Center",
                MaxMemberCount = 250,
                IsPrivate = true
            }),
            ChangedFieldsJson = JsonSerializer.Serialize(new[]
            {
                new ClubVersionFieldChange
                {
                    Field = "name",
                    OldValue = "Old Club",
                    NewValue = "Versioned Club"
                }
            }),
            ActorUserId = harness.OwnerUserId,
            ActorRole = harness.OwnerRole,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        var detail = await harness.Service.GetVersionDetailAsync(
            club.Id,
            2,
            harness.OwnerUserId,
            harness.OwnerRole);

        detail.VersionNumber.Should().Be(2);
        detail.Snapshot.Name.Should().Be("Versioned Club");
        detail.Snapshot.Clubtype.Should().Be(ClubType.Cultural.ToString());
        detail.ChangedFields.Should().ContainSingle().Which.Field.Should().Be("name");
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRestoreSnapshot_CreateVersion_AndRefreshCaches()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 181, userId: harness.OwnerUserId);
        club.Name = "Current Club";
        club.Description = "Current description";
        club.CurrentVersionNumber = 2;
        await harness.Db.SaveChangesAsync();

        harness.Db.ClubVersions.Add(new ClubVersion
        {
            ClubId = club.Id,
            VersionNumber = 1,
            ActionType = ClubVersionActions.Create,
            SnapshotJson = JsonSerializer.Serialize(new ClubVersionSnapshot
            {
                Name = "Original Club",
                Description = "Original description",
                Clubtype = ClubType.Gaming.ToString(),
                ClubImage = club.ClubImage,
                Phone = club.Phone,
                Email = club.Email,
                WebsiteUrl = club.WebsiteUrl,
                Location = club.Location,
                MaxMemberCount = club.MaxMemberCount,
                IsPrivate = club.isPrivate
            }),
            ChangedFieldsJson = "[]",
            ActorUserId = harness.OwnerUserId,
            ActorRole = harness.OwnerRole,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        var rollback = await harness.Service.RollbackToVersionAsync(
            club.Id,
            1,
            harness.OwnerUserId,
            harness.OwnerRole);

        rollback.RestoredFromVersionNumber.Should().Be(1);
        rollback.NewVersionNumber.Should().Be(3);
        rollback.Club.Name.Should().Be("Original Club");
        rollback.Club.Description.Should().Be("Original description");
        rollback.Club.CurrentVersionNumber.Should().Be(3);

        harness.OutboxWriterMock.Verify(writer => writer.StageUpsert(
            It.Is<Club>(entity => entity.Id == club.Id && entity.CurrentVersionNumber == 3)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.SetAsync(
            $"club:{club.Id}",
            It.Is<ClubCacheDto>(dto => dto.Id == club.Id && dto.Name == "Original Club"),
            It.IsAny<TimeSpan>(),
            It.IsAny<JsonSerializerOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetClub_ShouldReturnCachedClub_AndTrackAccessHotWindow()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = harness.BuildClub(id: 191, userId: harness.OwnerUserId);
        harness.SetupCachedClub(club);

        var expirySet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.CacheMock
            .Setup(cache => cache.IncrementAsync($"club:hot:count:{club.Id}", It.IsAny<long>()))
            .ReturnsAsync(1L);
        harness.CacheMock
            .Setup(cache => cache.SetExpiryAsync($"club:hot:count:{club.Id}", It.IsAny<TimeSpan>()))
            .Callback(() => expirySet.TrySetResult())
            .ReturnsAsync(true);

        var result = await harness.Service.GetClub(club.Id);
        await expirySet.Task.WaitAsync(TimeSpan.FromSeconds(2));

        result.Should().BeSameAs(club);
        harness.CacheMock.Verify(cache => cache.KeyExistsAsync($"club:hot:{club.Id}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.IncrementAsync($"club:hot:count:{club.Id}", 1), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetExpiryAsync($"club:hot:count:{club.Id}", It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetClub_ShouldThrowNotFound_WhenCacheMissResolvesToNull()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.RefreshCacheMock
            .Setup(cache => cache.GetOrSetAsync<Club, ClubCacheDto>(
                "club:999",
                It.IsAny<Func<Task<Club?>>>(),
                It.IsAny<Func<Club, ClubCacheDto>>(),
                It.IsAny<Func<ClubCacheDto, Club>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>()))
            .ReturnsAsync((Club?)null);

        var action = () => harness.Service.GetClub(999);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Club 999 not found");
    }

    [Fact]
    public async Task GetClubAccessAsync_ShouldReturnEmptyAccess_ForAnonymousUser()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 192, userId: harness.OwnerUserId);

        var access = await harness.Service.GetClubAccessAsync(club.Id, null);

        access.IsOwner.Should().BeFalse();
        access.IsManager.Should().BeFalse();
        access.IsVolunteer.Should().BeFalse();
        access.CanManage.Should().BeFalse();
    }

    [Fact]
    public async Task CanManageClubPostsAsync_AndCanManageEventMediaAsync_ShouldDelegateToStaffAccess()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 193, userId: harness.OtherOwnerUserId);
        harness.Db.ClubStaff.Add(new ClubStaff
        {
            ClubId = club.Id,
            UserId = harness.MemberUserId,
            Role = ClubStaffRole.Volunteer,
            GrantedByUserId = harness.OtherOwnerUserId
        });
        await harness.Db.SaveChangesAsync();

        (await harness.Service.CanManageClubPostsAsync(club.Id, harness.MemberUserId)).Should().BeTrue();
        (await harness.Service.CanManageEventMediaAsync(club.Id, harness.MemberUserId)).Should().BeTrue();
    }

    [Fact]
    public async Task AddStaffAsync_ShouldRejectOwnerAndDuplicateAssignments()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 194, userId: harness.OwnerUserId);
        harness.Db.ClubStaff.Add(new ClubStaff
        {
            ClubId = club.Id,
            UserId = harness.MemberUserId,
            Role = ClubStaffRole.Volunteer,
            GrantedByUserId = harness.OwnerUserId
        });
        await harness.Db.SaveChangesAsync();

        var ownerAction = () => harness.Service.AddStaffAsync(
            club.Id,
            harness.OwnerUserId,
            ClubStaffRole.Manager,
            harness.OwnerUserId,
            harness.OwnerRole);
        var duplicateAction = () => harness.Service.AddStaffAsync(
            club.Id,
            harness.MemberUserId,
            ClubStaffRole.Manager,
            harness.OwnerUserId,
            harness.OwnerRole);

        await ownerAction.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("The club owner already has full access.");
        await duplicateAction.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("User already has a staff role for this club.");
    }

    [Fact]
    public async Task RemoveStaffAsync_ShouldRejectOwnerRemoval_AndMissingAssignments()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 195, userId: harness.OwnerUserId);

        var ownerAction = () => harness.Service.RemoveStaffAsync(
            club.Id,
            harness.OwnerUserId,
            harness.OwnerUserId,
            harness.OwnerRole);
        var missingAction = () => harness.Service.RemoveStaffAsync(
            club.Id,
            harness.MemberUserId,
            harness.OwnerUserId,
            harness.OwnerRole);

        await ownerAction.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("The club owner cannot be removed from staff.");
        await missingAction.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Staff assignment was not found.");
    }

    [Fact]
    public async Task TransferOwnershipAsync_ShouldRejectCurrentOwner()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        var club = await harness.SeedPersistedClubAsync(id: 196, userId: harness.OwnerUserId);

        var action = () => harness.Service.TransferOwnershipAsync(
            club.Id,
            harness.OwnerUserId,
            harness.OwnerUserId,
            harness.OwnerRole);

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("This user already owns the club.");
    }

    [Fact]
    public async Task EventCreatedAsync_AndEventDeletedAsync_ShouldThrowNotImplemented()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var createAction = () => harness.Service.EventCreatedAsync(1, 2);
        var deleteAction = () => harness.Service.EventDeletedAsync(1, 2);

        await createAction.Should().ThrowAsync<backend.main.shared.exceptions.http.NotImplementedException>();
        await deleteAction.Should().ThrowAsync<backend.main.shared.exceptions.http.NotImplementedException>();
    }

    [Fact]
    public async Task GetClubListVersionAsync_ShouldInitializeMissingValue_AndReuseExistingValue()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.CacheMock
            .SetupSequence(cache => cache.GetValueAsync("clubs:version"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync("9");

        var first = await InvokePrivateAsync<long>(harness.Service, "GetClubListVersionAsync");
        var second = await InvokePrivateAsync<long>(harness.Service, "GetClubListVersionAsync");

        first.Should().Be(1);
        second.Should().Be(9);
        harness.CacheMock.Verify(cache => cache.SetValueAsync("clubs:version", "1", null), Times.Once);
    }

    [Fact]
    public async Task TrackClubAccessAsync_ShouldSetHotFlag_WhenThresholdIsReached()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();
        harness.CacheMock
            .Setup(cache => cache.KeyExistsAsync("club:hot:77"))
            .ReturnsAsync(false);
        harness.CacheMock
            .Setup(cache => cache.IncrementAsync("club:hot:count:77", It.IsAny<long>()))
            .ReturnsAsync(5L);

        await InvokePrivateAsync<object?>(harness.Service, "TrackClubAccessAsync", 77);

        harness.CacheMock.Verify(cache => cache.SetValueAsync("club:hot:77", "1", It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public void WithJitter_ShouldStayWithinConfiguredPercentRange()
    {
        var baseTtl = TimeSpan.FromSeconds(10);

        var jittered = InvokePrivateStatic<TimeSpan>(
            typeof(ClubService),
            "WithJitter",
            baseTtl,
            20);

        jittered.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(8));
        jittered.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void NormalizePageSize_ShouldClampToSupportedRange()
    {
        InvokePrivateStatic<int>(typeof(ClubService), "NormalizePageSize", 0).Should().Be(20);
        InvokePrivateStatic<int>(typeof(ClubService), "NormalizePageSize", 101).Should().Be(100);
        InvokePrivateStatic<int>(typeof(ClubService), "NormalizePageSize", 25).Should().Be(25);
    }

    [Fact]
    public void ParseClubType_ShouldMapAliases_AndFallbackToEnumParsing()
    {
        InvokePrivateStatic<ClubType>(typeof(ClubService), "ParseClubType", "sport")
            .Should().Be(ClubType.Sports);
        InvokePrivateStatic<ClubType>(typeof(ClubService), "ParseClubType", "academic")
            .Should().Be(ClubType.Academic);
        InvokePrivateStatic<ClubType>(typeof(ClubService), "ParseClubType", "social")
            .Should().Be(ClubType.Social);
        InvokePrivateStatic<ClubType>(typeof(ClubService), "ParseClubType", "other")
            .Should().Be(ClubType.Other);
        InvokePrivateStatic<ClubType>(typeof(ClubService), "ParseClubType", "Gaming")
            .Should().Be(ClubType.Gaming);
    }

    private static async Task<T> InvokePrivateAsync<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var invocation = method!.Invoke(target, args);
        if (invocation is Task<T> typedTask)
            return await typedTask;

        var task = (Task)invocation!;
        await task;
        return default!;
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        return (T)method!.Invoke(null, args)!;
    }

    private sealed class ClubServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubService Service { get; }
        public Mock<IClubRepository> ClubRepositoryMock { get; } = new();
        public Mock<IUserService> UserServiceMock { get; } = new();
        public Mock<IFollowService> FollowServiceMock { get; } = new();
        public Mock<IAzureBlobService> BlobServiceMock { get; } = new();
        public Mock<ICacheService> CacheMock { get; } = new();
        public Mock<IRefreshAheadCache> RefreshCacheMock { get; } = new();
        public Mock<IClubSearchService> SearchServiceMock { get; } = new();
        public Mock<IClubSearchOutboxWriter> OutboxWriterMock { get; } = new();

        public int OwnerUserId => 7;
        public int OtherOwnerUserId => 8;
        public int MemberUserId => 9;
        public string OwnerRole => "Organizer";

        private ClubServiceHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;

            CacheMock
                .Setup(cache => cache.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            CacheMock
                .Setup(cache => cache.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1L);
            CacheMock
                .Setup(cache => cache.GetManyAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string?>());
            CacheMock
                .Setup(cache => cache.KeyExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            CacheMock
                .Setup(cache => cache.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            RefreshCacheMock
                .Setup(cache => cache.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            RefreshCacheMock
                .Setup(cache => cache.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<ClubCacheDto>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<JsonSerializerOptions?>()))
                .Returns(Task.CompletedTask);

            FollowServiceMock
                .Setup(service => service.IsMemberAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(false);
            FollowServiceMock
                .Setup(service => service.AddMembershipAsync(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask);
            FollowServiceMock
                .Setup(service => service.RemoveMembershipAsync(It.IsAny<int>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            BlobServiceMock
                .Setup(service => service.DeleteBlobAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            BlobServiceMock
                .Setup(service => service.IsOwnedBlobUrl(It.Is<string>(url => url.StartsWith("https://cdn.test/clubs/", StringComparison.Ordinal))))
                .Returns(true);

            Service = new ClubService(
                db,
                ClubRepositoryMock.Object,
                UserServiceMock.Object,
                BlobServiceMock.Object,
                FollowServiceMock.Object,
                CacheMock.Object,
                RefreshCacheMock.Object,
                SearchServiceMock.Object,
                OutboxWriterMock.Object,
                Options.Create(new ClubVersioningOptions()),
                TimeProvider.System);
        }

        public static async Task<ClubServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new User
                {
                    Id = 7,
                    Email = "owner@test.local",
                    Usertype = "Organizer"
                },
                new User
                {
                    Id = 8,
                    Email = "other-owner@test.local",
                    Usertype = "Organizer"
                },
                new User
                {
                    Id = 9,
                    Email = "member@test.local",
                    Usertype = "Participant"
                });
            await db.SaveChangesAsync();

            return new ClubServiceHarness(connection, db);
        }

        public Club BuildClub(
            int id = 5,
            string name = "Board Games Club",
            int userId = 7,
            int memberCount = 0,
            ClubType clubType = ClubType.Gaming)
        {
            return new Club
            {
                Id = id,
                Name = name,
                Description = "A club for tabletop games and community events.",
                Clubtype = clubType,
                ClubImage = $"https://cdn.test/clubs/{id}.png",
                Phone = "555-0000",
                Email = $"club{id}@test.local",
                UserId = userId,
                MemberCount = memberCount,
                CurrentVersionNumber = 1
            };
        }

        public void ConfigureClubPersistence()
        {
            var repository = new ClubRepository(Db);

            ClubRepositoryMock
                .Setup(repo => repo.GetByIdAsync(It.IsAny<int>()))
                .Returns((int clubId) => repository.GetByIdAsync(clubId));

            ClubRepositoryMock
                .Setup(repo => repo.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => repository.GetByIdsAsync(ids));

            ClubRepositoryMock
                .Setup(repo => repo.SearchAsync(It.IsAny<ClubSearchCriteria>()))
                .Returns((ClubSearchCriteria criteria) => repository.SearchAsync(criteria));

            ClubRepositoryMock
                .Setup(repo => repo.UpdateAsync(It.IsAny<int>(), It.IsAny<Club>()))
                .Returns((int clubId, Club club) => repository.UpdateAsync(clubId, club));
        }

        public void SetupCachedClub(Club club)
        {
            RefreshCacheMock
                .Setup(cache => cache.GetOrSetAsync<Club, ClubCacheDto>(
                    $"club:{club.Id}",
                    It.IsAny<Func<Task<Club?>>>(),
                    It.IsAny<Func<Club, ClubCacheDto>>(),
                    It.IsAny<Func<ClubCacheDto, Club>>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<double>()))
                .ReturnsAsync(club);
        }

        public async Task<Club> SeedPersistedClubAsync(
            int id = 15,
            int userId = 7,
            int memberCount = 0,
            ClubType clubType = ClubType.Gaming)
        {
            var club = BuildClub(id, $"Club {id}", userId, memberCount, clubType);
            Db.Clubs.Add(club);
            await Db.SaveChangesAsync();
            return club;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
