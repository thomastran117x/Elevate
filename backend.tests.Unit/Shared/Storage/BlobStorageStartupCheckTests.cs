using backend.main.application.features;
using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

public class BlobStorageStartupCheckTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldCheckTheQuarantineContainer_OnlyWhileQuarantineIsOn(bool quarantineOn)
    {
        var blobs = new Mock<IAzureBlobService>();
        blobs.Setup(b => b.FindMissingContainersAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["event-assets-quarantine"]);

        await RunOnceAsync(blobs.Object, quarantineOn);

        blobs.Verify(b => b.FindMissingContainersAsync(quarantineOn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldReportRatherThanFail_WhenTheCheckItselfFails()
    {
        // Missing credentials or an unreachable account must not stop the API from starting.
        var blobs = new Mock<IAzureBlobService>();
        blobs.Setup(b => b.FindMissingContainersAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unreachable"));

        var act = () => RunOnceAsync(blobs.Object, quarantineOn: true);

        await act.Should().NotThrowAsync();
    }

    private static async Task RunOnceAsync(IAzureBlobService blobService, bool quarantineOn)
    {
        var services = new ServiceCollection();
        services.AddSingleton(blobService);
        await using var provider = services.BuildServiceProvider();
        var flags = new Mock<IFeatureFlagEvaluator>();
        flags.Setup(f => f.IsEnabled(FeatureFlagKeys.StorageQuarantine)).Returns(quarantineOn);

        var check = new BlobStorageStartupCheck(provider, flags.Object);
        await check.StartAsync(CancellationToken.None);
        await (check.ExecuteTask ?? Task.CompletedTask);
        await check.StopAsync(CancellationToken.None);
    }
}
