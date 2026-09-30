using backend.main.features.clubs;
using backend.main.features.media;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Media;

public class MediaAssetQueryServiceTests
{
    [Fact]
    public async Task GetForViewerAsync_ShouldShowTheUploaderTheirAsset()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Processing, ownerUserId: owner.Id);

        var response = await CreateService(database).GetForViewerAsync(asset.PublicId, owner.Id, "Organizer");

        response.Id.Should().Be(asset.PublicId);
        response.Status.Should().Be(MediaAssetStatus.Processing);
        response.Url.Should().BeNull("nothing is published until the asset is Ready");
        response.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task GetForViewerAsync_ShouldIncludeTheUrl_OnlyOnceReady()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Ready, ownerUserId: owner.Id);

        var response = await CreateService(database).GetForViewerAsync(asset.PublicId, owner.Id, null);

        response.Url.Should().Be(asset.PublicUrl);
    }

    [Fact]
    public async Task GetForViewerAsync_ShouldIncludeTheReason_ForARejectedAsset()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Processing, ownerUserId: owner.Id);
        await database.CreateRepository().TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Processing,
            MediaAssetStatus.Rejected,
            new MediaAssetChanges { RejectionReason = "Animated images are not supported." });

        var response = await CreateService(database).GetForViewerAsync(asset.PublicId, owner.Id, null);

        response.Status.Should().Be(MediaAssetStatus.Rejected);
        response.RejectionReason.Should().Be("Animated images are not supported.");
        response.Url.Should().BeNull();
    }

    [Fact]
    public async Task GetForViewerAsync_ShouldShowAManagerOfTheAssetsClub()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var manager = await database.SeedUserAsync("manager");
        var club = await database.SeedClubAsync(owner.Id);
        var asset = await database.SeedAssetAsync(ownerUserId: owner.Id, clubId: club.Id);
        var clubs = new Mock<IClubService>();
        clubs.Setup(c => c.CanManageClubAsync(club.Id, manager.Id, "Organizer")).ReturnsAsync(true);

        var response = await CreateService(database, clubs.Object).GetForViewerAsync(asset.PublicId, manager.Id, "Organizer");

        response.Id.Should().Be(asset.PublicId);
    }

    [Fact]
    public async Task GetForViewerAsync_ShouldAnswerNotFound_ToAnyoneElse_SoIdsCannotBeProbed()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var stranger = await database.SeedUserAsync("stranger");
        var club = await database.SeedClubAsync(owner.Id);
        var asset = await database.SeedAssetAsync(ownerUserId: owner.Id, clubId: club.Id);
        var service = CreateService(database);

        var someoneElses = () => service.GetForViewerAsync(asset.PublicId, stranger.Id, "Organizer");
        var nobodys = () => service.GetForViewerAsync(Guid.NewGuid(), stranger.Id, "Organizer");

        (await someoneElses.Should().ThrowAsync<ResourceNotFoundException>()).Which.Message
            .Should().Be((await nobodys.Should().ThrowAsync<ResourceNotFoundException>()).Which.Message);
    }

    [Fact]
    public async Task GetForViewerAsync_ShouldNotConsultClubs_ForAnAssetWithNoClub()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var owner = await database.SeedUserAsync();
        var stranger = await database.SeedUserAsync("stranger");
        var asset = await database.SeedAssetAsync(ownerUserId: owner.Id);
        var clubs = new Mock<IClubService>(MockBehavior.Strict);

        var act = () => CreateService(database, clubs.Object).GetForViewerAsync(asset.PublicId, stranger.Id, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>();
    }

    private static MediaAssetQueryService CreateService(MediaTestDatabase database, IClubService? clubs = null) =>
        new(database.CreateRepository(), clubs ?? Mock.Of<IClubService>());
}
