using backend.main.features.bloom;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth;

/// <inheritdoc cref="IUsernameAvailabilityService"/>
public sealed class UsernameAvailabilityService : IUsernameAvailabilityService
{
    private readonly IAuthUserRepository _repository;
    private readonly IBloomFilterRegistry _bloomFilters;

    public UsernameAvailabilityService(
        IAuthUserRepository repository,
        IBloomFilterRegistry bloomFilters)
    {
        _repository = repository;
        _bloomFilters = bloomFilters;
    }

    public async Task<bool> IsUnavailableAsync(
        string normalizedUsername,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // DefinitelyAbsent is the only answer that permits skipping the query, and it is exact:
        // a bloom filter has no false negatives, so a clear bit proves the name was never added.
        // PossiblyPresent and Unavailable both fall through to the database.
        if (_bloomFilters.MightContain(BloomFilterTargets.Username, normalizedUsername)
            == BloomFilterLookup.DefinitelyAbsent)
        {
            return false;
        }

        return await _repository.UsernameUnavailableAsync(normalizedUsername, utcNow);
    }

    public async Task MarkTakenAsync(
        string normalizedUsername,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(normalizedUsername))
            return;

        try
        {
            await _bloomFilters.AddAsync(BloomFilterTargets.Username, normalizedUsername, cancellationToken);
        }
        catch (Exception exception)
        {
            // Callers invoke this after the claiming write has already committed, and AuthService
            // converts any non-AppException into a 500 — so letting this throw would report a
            // successful signup as a server error. A missed bit only costs accuracy until the
            // next rebuild, which is strictly the lesser failure.
            Logger.Warn(
                exception,
                $"[UsernameAvailabilityService] Failed to record '{normalizedUsername}' in the username filter.");
        }
    }
}
