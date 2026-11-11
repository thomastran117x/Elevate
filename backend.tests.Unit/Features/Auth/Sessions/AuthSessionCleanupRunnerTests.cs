using backend.main.features.auth.sessions;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.Sessions;

public class AuthSessionCleanupRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnceAsync_ShouldPurgeRowsADayPastExpiry_UntilABatchComesBackShort()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        sessions.SetupSequence(s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), AuthSessionCleanupRunner.BatchSize))
            .ReturnsAsync(AuthSessionCleanupRunner.BatchSize)
            .ReturnsAsync(3);

        await new AuthSessionCleanupRunner(sessions.Object, new FixedClock(Now)).RunOnceAsync();

        sessions.Verify(
            s => s.DeleteExpiredAsync(Now.UtcDateTime.AddDays(-1), AuthSessionCleanupRunner.BatchSize),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStopAfterTheBatchLimit()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        sessions.Setup(s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(AuthSessionCleanupRunner.BatchSize);

        await new AuthSessionCleanupRunner(sessions.Object, new FixedClock(Now)).RunOnceAsync();

        sessions.Verify(
            s => s.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>()),
            Times.Exactly(AuthSessionCleanupRunner.MaxBatchesPerRun));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldDoNothing_WhenAlreadyCancelled()
    {
        var sessions = new Mock<IAuthSessionRepository>();

        await new AuthSessionCleanupRunner(sessions.Object, new FixedClock(Now))
            .RunOnceAsync(new CancellationToken(canceled: true));

        sessions.VerifyNoOtherCalls();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
