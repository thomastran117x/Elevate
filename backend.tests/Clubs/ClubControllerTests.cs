using System.Security.Claims;

using backend.main.features.clubs;
using backend.main.features.clubs.contracts.requests;
using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.clubs.versions;
using backend.main.features.clubs.versions.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using Xunit;

namespace backend.tests.Clubs;

public class ClubControllerTests
{
    [Fact]
    public async Task GetClubs_ShouldReturnPagedSearchResponse()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.GetAllClubs(It.Is<ClubSearchCriteria>(criteria =>
                criteria.Query == "chess" &&
                criteria.ClubType == ClubType.Social &&
                criteria.SortBy == ClubSortBy.Members &&
                criteria.Page == 2 &&
                criteria.PageSize == 10)))
            .ReturnsAsync((
                new List<Club>
                {
                    new()
                    {
                        Id = 4,
                        UserId = 7,
                        Name = "Chess Club",
                        Description = "Weekly strategy nights",
                        Clubtype = ClubType.Social,
                        ClubImage = "https://cdn.test/clubs/chess.png",
                        MemberCount = 18,
                        Location = "Student Center",
                        CurrentVersionNumber = 2,
                    }
                },
                11,
                "elasticsearch"));
        service.Setup(s => s.GetClubAccessMapAsync(It.IsAny<IEnumerable<int>>(), 7, "Organizer"))
            .ReturnsAsync(new Dictionary<int, ClubAccessInfo>
            {
                [4] = new()
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetClubs("chess", ClubType.Social, ClubSortBy.Members, 2, 10);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubResponse>>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.TotalCount.Should().Be(11);
        response.Data.Page.Should().Be(2);
        response.Data.PageSize.Should().Be(10);
        response.Meta.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchClubs_ShouldReturnPagedSearchResponse()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.GetAllClubs(It.Is<ClubSearchCriteria>(criteria =>
                criteria.Query == "robotics" &&
                criteria.ClubType == ClubType.Academic &&
                criteria.SortBy == ClubSortBy.Newest &&
                criteria.Page == 1 &&
                criteria.PageSize == 20)))
            .ReturnsAsync((
                new List<Club>
                {
                    new()
                    {
                        Id = 8,
                        UserId = 9,
                        Name = "Robotics Club",
                        Description = "Build nights",
                        Clubtype = ClubType.Academic,
                        ClubImage = "https://cdn.test/clubs/robotics.png",
                        CurrentVersionNumber = 1,
                    }
                },
                1,
                "database"));
        service.Setup(s => s.GetClubAccessMapAsync(It.IsAny<IEnumerable<int>>(), 7, "Organizer"))
            .ReturnsAsync(new Dictionary<int, ClubAccessInfo>
            {
                [8] = new()
            });

        var controller = CreateController(service.Object);

        var result = await controller.SearchClubs(new ClubSearchRequest
        {
            Query = "robotics",
            Filters = new ClubSearchFilters { ClubType = ClubType.Academic },
            SortBy = ClubSortBy.Newest,
            Page = 1,
            PageSize = 20
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubResponse>>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().ContainSingle();
        response.Data.Items.Single().Name.Should().Be("Robotics Club");
    }

    [Fact]
    public async Task GetClubVersions_ShouldReturnPagedHistoryResponse()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.GetVersionHistoryAsync(4, 7, "Organizer", 1, 20))
            .ReturnsAsync((
                new List<ClubVersionHistoryItem>
                {
                    new(
                        4,
                        2,
                        ClubVersionActions.Update,
                        new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc),
                        7,
                        "Organizer",
                        true,
                        new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
                        null,
                        [new ClubVersionFieldChange
                        {
                            Field = "name",
                            OldValue = "Chess Club",
                            NewValue = "Campus Chess Club"
                        }])
                },
                1));

        var controller = CreateController(service.Object);

        var result = await controller.GetClubVersions(4, 1, 20);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubVersionListItemResponse>>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().ContainSingle();
        response.Data.Items.Single().VersionNumber.Should().Be(2);
        response.Data.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task RollbackClubVersion_ShouldReturnRollbackPayload()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.RollbackToVersionAsync(4, 1, 7, "Organizer"))
            .ReturnsAsync(new ClubRollbackResult(
                new Club
                {
                    Id = 4,
                    UserId = 7,
                    Name = "Chess Club",
                    Description = "Weekly strategy nights",
                    Clubtype = ClubType.Social,
                    ClubImage = "https://cdn.test/clubs/club-v1.png",
                    CurrentVersionNumber = 3,
                },
                1,
                3));

        var controller = CreateController(service.Object);

        var result = await controller.RollbackClubVersion(4, 1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<ClubRollbackResponse>>().Subject;

        response.Data.Should().NotBeNull();
        response.Data!.RestoredFromVersionNumber.Should().Be(1);
        response.Data.NewVersionNumber.Should().Be(3);
        response.Data.Club.CurrentVersionNumber.Should().Be(3);
    }

    [Fact]
    public async Task GetManagedClubs_ShouldReturnOwnedAndManagedClubResponses()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.GetManagedClubsAsync(7))
            .ReturnsAsync([
                new Club
                {
                    Id = 4,
                    UserId = 7,
                    Name = "Chess Club",
                    Description = "Weekly strategy nights",
                    Clubtype = ClubType.Social,
                    ClubImage = "https://cdn.test/clubs/chess.png",
                    CurrentVersionNumber = 2,
                }
            ]);
        service.Setup(s => s.GetClubAccessMapAsync(It.IsAny<IEnumerable<int>>(), 7, "Organizer"))
            .ReturnsAsync(new Dictionary<int, ClubAccessInfo>
            {
                [4] = new() { IsOwner = true, CanManage = true }
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetManagedClubs();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<ClubResponse>>>().Subject;

        response.Data.Should().ContainSingle();
        response.Data!.Single().IsOwner.Should().BeTrue();
        response.Data.Single().CanManage.Should().BeTrue();
    }

    [Fact]
    public async Task AddManager_ShouldReturnCreatedStaffPayload()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.AddStaffAsync(4, 55, ClubStaffRole.Manager, 7, "Organizer"))
            .ReturnsAsync(new ClubStaff
            {
                Id = 3,
                ClubId = 4,
                UserId = 55,
                Role = ClubStaffRole.Manager,
                GrantedByUserId = 7,
                CreatedAt = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc)
            });

        var controller = CreateController(service.Object);

        var result = await controller.AddManager(4, new ClubStaffCreateRequest { UserId = 55 });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);

        var response = created.Value.Should().BeOfType<ApiResponse<ClubStaffResponse>>().Subject;
        response.Data.Should().NotBeNull();
        response.Data!.UserId.Should().Be(55);
        response.Data.Role.Should().Be("Manager");
    }

    [Fact]
    public async Task AddVolunteer_ShouldReturnCreatedStaffPayload()
    {
        var service = new Mock<IClubService>();
        service.Setup(s => s.AddStaffAsync(4, 66, ClubStaffRole.Volunteer, 7, "Organizer"))
            .ReturnsAsync(new ClubStaff
            {
                Id = 4,
                ClubId = 4,
                UserId = 66,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = 7,
                CreatedAt = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc)
            });

        var controller = CreateController(service.Object);

        var result = await controller.AddVolunteer(4, new ClubStaffCreateRequest { UserId = 66 });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);

        var response = created.Value.Should().BeOfType<ApiResponse<ClubStaffResponse>>().Subject;
        response.Data.Should().NotBeNull();
        response.Data!.UserId.Should().Be(66);
        response.Data.Role.Should().Be("Volunteer");
    }

    private static ClubController CreateController(IClubService service)
    {
        var controller = new ClubController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "owner@test.local"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
