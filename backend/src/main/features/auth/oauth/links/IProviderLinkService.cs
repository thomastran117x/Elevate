namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// Explicit provider link lifecycle for the signed-in owner: list, link, confirm an
    /// email-matched link, and unlink. Every change needs a fresh step-up, enforced by the caller.
    /// </summary>
    public interface IProviderLinkService
    {
        Task<ProviderLinkOverview> GetOverviewAsync(int userId);

        Task<ProviderLinkStart> StartAsync(ProviderLinkSession session, string provider);

        Task<UserIdentityLink> CompleteAsync(
            ProviderLinkSession session,
            string provider,
            string state,
            string? code,
            string? idToken);

        Task<UserIdentityLink> ConfirmEmailLinkAsync(int userId, string linkToken);

        Task UnlinkAsync(int userId, string provider);
    }

    /// <summary>The caller's identity as its access token states it.</summary>
    public sealed record ProviderLinkSession(int UserId, string SessionId, int AuthVersion);

    public sealed record ProviderLinkOverview(bool HasPassword, IReadOnlyList<ProviderLinkStatus> Providers);

    /// <param name="Available">Whether the provider is configured, so a link can be started.</param>
    /// <param name="CanUnlink">Whether removing <paramref name="Link"/> leaves another way to sign in.</param>
    public sealed record ProviderLinkStatus(
        string Provider,
        bool Available,
        UserIdentityLink? Link,
        bool CanUnlink);

    /// <param name="CodeVerifier">Set only when the browser redeems the code itself.</param>
    public sealed record ProviderLinkStart(string AuthorizationUrl, string State, string? CodeVerifier);
}
