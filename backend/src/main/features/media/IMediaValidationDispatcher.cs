namespace backend.main.features.media;

/// <summary>
/// Gets a claimed asset validated. <c>storage.inlinevalidation</c> picks the implementation: run
/// the pipeline here and now, or hand the asset to media-worker and let its result arrive later.
/// </summary>
public interface IMediaValidationDispatcher
{
    /// <summary>
    /// How long an attach waits for an asset it finds Processing to settle before reporting it
    /// as still being checked. Zero when <see cref="DispatchAsync"/> settles the asset itself.
    /// </summary>
    TimeSpan SettleWait
    {
        get;
    }

    /// <summary>How often an attach that is waiting re-reads the asset.</summary>
    TimeSpan PollInterval
    {
        get;
    }

    /// <summary>
    /// Starts validating <paramref name="asset"/> under claim <paramref name="attempt"/>, which
    /// the caller has just taken. Returns once the outcome is recorded, or once the work has been
    /// handed off; either way the caller reads the asset again to learn where it stands.
    /// </summary>
    /// <param name="subject">Names the upload in the size message — "Event images" or "Club images".</param>
    /// <exception cref="Exception">
    /// Anything other than a verdict on the bytes. The caller releases its claim and rethrows.
    /// </exception>
    Task DispatchAsync(MediaAsset asset, int attempt, string subject, CancellationToken cancellationToken);
}
