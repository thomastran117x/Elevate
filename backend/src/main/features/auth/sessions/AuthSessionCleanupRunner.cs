namespace backend.main.features.auth.sessions;

/// <summary>
/// Deletes session rows past their expiry.
/// <para>
/// A revoked row is kept until it expires as well. Until then its Redis session may still exist,
/// if the cleanup after the revocation never landed, and the row is what stops that session's
/// next refresh from recording it as a new one. Redis never keeps a session past the expiry
/// recorded here, so a day later nothing can be brought back.
/// </para>
/// <para>
/// Split from <see cref="AuthSessionCleanupService"/> so the work is a plain scoped class that
/// tests can run directly with a fake <see cref="TimeProvider"/>.
/// </para>
/// </summary>
public sealed class AuthSessionCleanupRunner
{
    internal static readonly TimeSpan ExpiredRowGrace = TimeSpan.FromDays(1);
    internal const int BatchSize = 500;
    internal const int MaxBatchesPerRun = 20;

    private readonly IAuthSessionRepository _sessions;
    private readonly TimeProvider _timeProvider;

    public AuthSessionCleanupRunner(IAuthSessionRepository sessions, TimeProvider timeProvider)
    {
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - ExpiredRowGrace;

        // Bounded rather than "until empty", so a large backlog does not hold one scoped
        // DbContext open indefinitely; whatever is left waits for the next pass.
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (await _sessions.DeleteExpiredAsync(cutoff, BatchSize) < BatchSize)
                return;
        }
    }
}
