using backend.main.features.auth.device;
using backend.main.features.auth.sessions;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.Sessions;

public class AuthSessionCleanupRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnceAsync_ShouldPurgeSessionsADayPastExpiry_UntilABatchComesBackShort()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        sessions.SetupSequence(s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), AuthSessionCleanupRunner.BatchSize))
            .ReturnsAsync(AuthSessionCleanupRunner.BatchSize)
            .ReturnsAsync(3);
        var devices = new Mock<IDeviceRepository>();

        await CreateRunner(sessions, devices).RunOnceAsync();

        sessions.Verify(
            s => s.DeleteExpiredAsync(Now.UtcDateTime.AddDays(-1), AuthSessionCleanupRunner.BatchSize),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldPurgeDevicesTheMomentTheirTrustEnds()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(2);

        await CreateRunner(sessions, devices).RunOnceAsync();

        devices.Verify(d => d.DeleteExpiredAsync(Now.UtcDateTime, AuthSessionCleanupRunner.BatchSize), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStopAfterTheBatchLimit()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        sessions.Setup(s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(AuthSessionCleanupRunner.BatchSize);
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(AuthSessionCleanupRunner.BatchSize);

        await CreateRunner(sessions, devices).RunOnceAsync();

        sessions.Verify(
            s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()),
            Times.Exactly(AuthSessionCleanupRunner.MaxBatchesPerRun));
        devices.Verify(
            d => d.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()),
            Times.Exactly(AuthSessionCleanupRunner.MaxBatchesPerRun));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldDoNothing_WhenAlreadyCancelled()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        var devices = new Mock<IDeviceRepository>();

        await CreateRunner(sessions, devices).RunOnceAsync(new CancellationToken(canceled: true));

        sessions.VerifyNoOtherCalls();
        devices.VerifyNoOtherCalls();
    }

    private static AuthSessionCleanupRunner CreateRunner(
        Mock<IAuthSessionRepository> sessions,
        Mock<IDeviceRepository> devices) =>
        new(sessions.Object, devices.Object, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
