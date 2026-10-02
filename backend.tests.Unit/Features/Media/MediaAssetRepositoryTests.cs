using backend.main.features.media;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Media;

public class MediaAssetRepositoryTests
{
    [Fact]
    public async Task TryTransitionAsync_ShouldMoveTheRowAndApplyChanges_WhenItIsInTheExpectedState()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Processing);
        database.Time.Advance(TimeSpan.FromMinutes(3));
        var validatedAt = database.Time.GetUtcNow().UtcDateTime;

        var moved = await database.CreateRepository().TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Processing,
            MediaAssetStatus.Ready,
            new MediaAssetChanges
            {
                ContentType = "image/webp",
                Width = 640,
                Height = 480,
                ByteSize = 1234,
                ValidatedAt = validatedAt,
                ClearQuarantineBlobPath = true
            });

        moved.Should().BeTrue();
        var stored = await database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Ready);
        stored.ContentType.Should().Be("image/webp");
        stored.Width.Should().Be(640);
        stored.Height.Should().Be(480);
        stored.ByteSize.Should().Be(1234);
        stored.ValidatedAt.Should().Be(validatedAt);
        stored.QuarantineBlobPath.Should().BeNull();
        stored.UpdatedAt.Should().Be(validatedAt);
        stored.PublicUrl.Should().Be(asset.PublicUrl, "columns the changes leave out are untouched");
    }

    [Fact]
    public async Task TryTransitionAsync_ShouldReturnFalseAndWriteNothing_WhenTheRowHasAlreadyMoved()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Processing);

        var moved = await database.CreateRepository().TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Uploaded,
            MediaAssetStatus.Processing,
            new MediaAssetChanges { CountAttempt = true });

        moved.Should().BeFalse();
        var stored = await database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Processing);
        stored.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task TryTransitionAsync_ShouldLetOnlyOneOfTwoRacingClaimsWin()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Uploaded);
        var repository = database.CreateRepository();

        var first = await repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing, new MediaAssetChanges { CountAttempt = true });
        var second = await repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing, new MediaAssetChanges { CountAttempt = true });

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await database.ReloadAsync(asset.Id)).AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task TryTransitionAsync_ShouldRequireTheExpectedAttempt_WhenOneIsGiven()
    {
        // The attempt count identifies a claim, so a holder whose claim was taken over (and the
        // count bumped) cannot finish it.
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Uploaded);
        var repository = database.CreateRepository();
        await repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing, new MediaAssetChanges { CountAttempt = true });

        var stale = await repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Ready, whenAttempt: 0);
        var current = await repository.TryTransitionAsync(
            asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Ready, whenAttempt: 1);

        stale.Should().BeFalse();
        current.Should().BeTrue();
        (await database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Ready);
    }

    [Fact]
    public async Task TryTransitionAsync_ShouldThrowBeforeTouchingTheDatabase_WhenTheMoveIsIllegal()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Ready);

        var act = () => database.CreateRepository().TryTransitionAsync(
            asset.Id, MediaAssetStatus.Ready, MediaAssetStatus.Processing);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Ready);
    }

    [Fact]
    public async Task TryTransitionAsync_ShouldNotFlushOtherTrackedChanges()
    {
        // The attach path runs this while the caller may hold a half-built event or club in the
        // same context. A SaveChanges here would commit it early.
        await using var database = await MediaTestDatabase.CreateAsync();
        var user = await database.SeedUserAsync();
        var asset = await database.SeedAssetAsync(MediaAssetStatus.Uploaded);

        var tracked = await database.Db.Users.SingleAsync(u => u.Id == user.Id);
        tracked.Name = "Not saved yet";

        await database.CreateRepository().TryTransitionAsync(
            asset.Id, MediaAssetStatus.Uploaded, MediaAssetStatus.Processing);

        var stored = await database.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.Name.Should().NotBe("Not saved yet");
    }

    [Fact]
    public async Task AddAsync_ShouldStageTheAssetWithoutSaving()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var repository = database.CreateRepository();
        var asset = new MediaAsset
        {
            PublicId = Guid.NewGuid(),
            Status = MediaAssetStatus.PendingUpload,
            Origin = MediaAssetOrigin.Upload,
            DeclaredContentType = "image/png"
        };

        await repository.AddAsync(asset);

        (await database.Db.MediaAssets.AsNoTracking().CountAsync()).Should().Be(0);
        await database.Db.SaveChangesAsync();
        (await repository.GetByPublicIdAsync(asset.PublicId)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetByQuarantineBlobPathAsync_ShouldFindTheAssetHoldingThatBlob()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(quarantineBlobPath: "events/clubs/1/pending/abc.png");
        await database.SeedAssetAsync();

        var found = await database.CreateRepository().GetByQuarantineBlobPathAsync("events/clubs/1/pending/abc.png");

        found!.Id.Should().Be(asset.Id);
        (await database.CreateRepository().GetByQuarantineBlobPathAsync("missing.png")).Should().BeNull();
    }

    [Fact]
    public async Task GetUnattachedIssuedBeforeAsync_ShouldReturnOnlyOldPendingAndUploadedAssets_OldestFirst()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var now = database.Time.GetUtcNow().UtcDateTime;
        var oldUploaded = await database.SeedAssetAsync(MediaAssetStatus.Uploaded, createdAt: now.AddHours(-30));
        var oldPending = await database.SeedAssetAsync(MediaAssetStatus.PendingUpload, createdAt: now.AddHours(-48));
        await database.SeedAssetAsync(MediaAssetStatus.PendingUpload, createdAt: now.AddHours(-1));
        await database.SeedAssetAsync(MediaAssetStatus.Processing, createdAt: now.AddHours(-48));
        await database.SeedAssetAsync(MediaAssetStatus.Ready, createdAt: now.AddHours(-48));

        var found = await database.CreateRepository().GetUnattachedIssuedBeforeAsync(now.AddHours(-24), limit: 10);

        found.Select(asset => asset.Id).Should().Equal(oldPending.Id, oldUploaded.Id);
    }

    [Fact]
    public async Task GetUnattachedIssuedBeforeAsync_ShouldRespectTheLimit()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var now = database.Time.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 3; i++)
            await database.SeedAssetAsync(createdAt: now.AddHours(-30 - i));

        var found = await database.CreateRepository().GetUnattachedIssuedBeforeAsync(now.AddHours(-24), limit: 2);

        found.Should().HaveCount(2);
    }
}
