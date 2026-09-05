using System.Runtime.CompilerServices;

using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.bloom;

/// <summary>
/// Populates the username filter from the two tables that jointly own the username namespace.
/// </summary>
/// <remarks>
/// A username is unavailable if a user holds it or a reservation still covers it, which is the
/// same predicate <c>AuthUserRepository.UsernameUnavailableAsync</c> evaluates. The filter must
/// stay a superset of that predicate, so both tables are read here; omitting reservations would
/// let the filter report a cooling-down name as absent.
/// </remarks>
public sealed class UsernameBloomFilterSource : IBloomFilterSource
{
    private readonly AppDatabaseContext _context;
    private readonly TimeProvider _clock;

    public UsernameBloomFilterSource(AppDatabaseContext context, TimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public string Target => BloomFilterTargets.Username;

    public async IAsyncEnumerable<string> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var usernames = _context.Users
            .AsNoTracking()
            .Where(user => user.Username != null)
            .Select(user => user.Username!)
            .AsAsyncEnumerable();

        await foreach (var username in usernames.WithCancellation(cancellationToken))
            yield return UsernamePolicy.Normalize(username);

        // Expired reservations are deliberately excluded: they no longer block a signup, and
        // carrying them would keep names that have been released looking taken until the next
        // rebuild. This is exactly the staleness a generation rebuild exists to clear.
        var now = _clock.GetUtcNow().UtcDateTime;
        var reserved = _context.UsernameReservations
            .AsNoTracking()
            .Where(reservation => reservation.ReservedUntilUtc > now)
            .Select(reservation => reservation.Username)
            .AsAsyncEnumerable();

        await foreach (var username in reserved.WithCancellation(cancellationToken))
            yield return UsernamePolicy.Normalize(username);
    }
}
