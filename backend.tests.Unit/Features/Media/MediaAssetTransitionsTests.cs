using backend.main.features.media;

using FluentAssertions;

namespace backend.tests.Unit.Features.Media;

public class MediaAssetTransitionsTests
{
    private static readonly (MediaAssetStatus From, MediaAssetStatus To)[] Legal =
    [
        (MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded),
        (MediaAssetStatus.PendingUpload, MediaAssetStatus.Rejected),
        (MediaAssetStatus.Uploaded, MediaAssetStatus.Processing),
        (MediaAssetStatus.Uploaded, MediaAssetStatus.Rejected),
        (MediaAssetStatus.Processing, MediaAssetStatus.Ready),
        (MediaAssetStatus.Processing, MediaAssetStatus.Rejected),
        (MediaAssetStatus.Processing, MediaAssetStatus.NeedsReview),
        (MediaAssetStatus.Processing, MediaAssetStatus.Uploaded),
        (MediaAssetStatus.NeedsReview, MediaAssetStatus.Ready),
        (MediaAssetStatus.NeedsReview, MediaAssetStatus.Rejected)
    ];

    public static TheoryData<MediaAssetStatus, MediaAssetStatus> EveryPair()
    {
        var data = new TheoryData<MediaAssetStatus, MediaAssetStatus>();
        foreach (var from in Enum.GetValues<MediaAssetStatus>())
        {
            foreach (var to in Enum.GetValues<MediaAssetStatus>())
                data.Add(from, to);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void IsAllowed_ShouldMatchTheDocumentedTable_ForEveryPair(MediaAssetStatus from, MediaAssetStatus to)
    {
        MediaAssetTransitions.IsAllowed(from, to).Should().Be(Legal.Contains((from, to)));
    }

    [Fact]
    public void EnsureAllowed_ShouldThrow_WhenAReadyAssetIsSentBackToProcessing()
    {
        var act = () => MediaAssetTransitions.EnsureAllowed(MediaAssetStatus.Ready, MediaAssetStatus.Processing);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ready*Processing*");
    }

    [Theory]
    [InlineData(MediaAssetStatus.Ready, true)]
    [InlineData(MediaAssetStatus.Rejected, true)]
    [InlineData(MediaAssetStatus.PendingUpload, false)]
    [InlineData(MediaAssetStatus.Processing, false)]
    [InlineData(MediaAssetStatus.NeedsReview, false)]
    public void IsTerminal_ShouldHoldOnlyForReadyAndRejected(MediaAssetStatus status, bool terminal)
    {
        MediaAssetTransitions.IsTerminal(status).Should().Be(terminal);
    }

    [Fact]
    public void TransitionTo_ShouldMoveTheAssetAndStampUpdatedAt_WhenLegal()
    {
        var asset = new MediaAsset { DeclaredContentType = "image/png", Status = MediaAssetStatus.PendingUpload };
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        asset.TransitionTo(MediaAssetStatus.Uploaded, now);

        asset.Status.Should().Be(MediaAssetStatus.Uploaded);
        asset.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void TransitionTo_ShouldThrowAndLeaveTheAssetAlone_WhenIllegal()
    {
        var before = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var asset = new MediaAsset
        {
            DeclaredContentType = "image/png",
            Status = MediaAssetStatus.Ready,
            UpdatedAt = before
        };

        var act = () => asset.TransitionTo(MediaAssetStatus.Processing, DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
        asset.Status.Should().Be(MediaAssetStatus.Ready);
        asset.UpdatedAt.Should().Be(before);
    }

    [Fact]
    public void StatusValues_ShouldKeepTheirPositions_BecauseClientsDecodeThemByIndex()
    {
        // Appending is fine; reordering or inserting renumbers every value after it under clients
        // that decode the API's integer by array position.
        Enum.GetValues<MediaAssetStatus>().Select(status => (int)status).Should().Equal(0, 1, 2, 3, 4, 5);
        ((int)MediaAssetStatus.Ready).Should().Be(3);
        ((int)MediaAssetStatus.Rejected).Should().Be(4);
    }
}
