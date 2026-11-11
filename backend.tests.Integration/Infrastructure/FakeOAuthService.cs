using System.Collections.Concurrent;

using backend.main.features.auth.oauth;
using backend.main.shared.exceptions.http;

namespace backend.tests.Integration.Infrastructure;

/// <summary>
/// Stands in for Google and Microsoft. A token is registered with the identity it proves and,
/// optionally, the nonce it carries; verification enforces that nonce the way the real service
/// does, so a token without one fails wherever a nonce is expected. A Google code exchange
/// returns the code itself as the ID token.
/// </summary>
public sealed class FakeOAuthService : IOAuthService
{
    private readonly ConcurrentDictionary<string, (OAuthUser User, string? Nonce)> _googleTokens = new();
    private readonly ConcurrentDictionary<string, (OAuthUser User, string? Nonce)> _microsoftTokens = new();
    private readonly ConcurrentQueue<(string Code, string CodeVerifier, string RedirectUri)> _googleExchanges = new();

    public void RegisterGoogleToken(string token, OAuthUser user, string? nonce = null) =>
        _googleTokens[token] = (user, nonce);

    public void RegisterMicrosoftToken(string token, OAuthUser user, string? nonce = null) =>
        _microsoftTokens[token] = (user, nonce);

    /// <summary>Every Google code exchange so far, with the verifier and redirect it was given.</summary>
    public IReadOnlyCollection<(string Code, string CodeVerifier, string RedirectUri)> GoogleExchanges =>
        _googleExchanges.ToArray();

    public void Clear()
    {
        _googleTokens.Clear();
        _microsoftTokens.Clear();
        _googleExchanges.Clear();
    }

    public Task<string> ExchangeGoogleCodeAsync(string code, string codeVerifier, string redirectUri)
    {
        _googleExchanges.Enqueue((code, codeVerifier, redirectUri));
        return Task.FromResult(code);
    }

    public Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken, string? expectedNonce = null) =>
        Verify(_googleTokens, googleToken, expectedNonce, "Google");

    public Task<OAuthUser> VerifyMicrosoftTokenAsync(string microsoftToken, string? expectedNonce = null) =>
        Verify(_microsoftTokens, microsoftToken, expectedNonce, "Microsoft");

    public bool IsProviderConfigured(string provider) => provider is "google" or "microsoft";

    public Task<OAuthUser> VerifyAppleTokenAsync(string appleToken)
    {
        throw new NotSupportedException("Apple OAuth is not used in tests.");
    }

    private static Task<OAuthUser> Verify(
        ConcurrentDictionary<string, (OAuthUser User, string? Nonce)> tokens,
        string token,
        string? expectedNonce,
        string provider)
    {
        if (!tokens.TryGetValue(token, out var registered))
            throw new UnauthorizedException($"Invalid {provider} Token");

        if (!string.IsNullOrWhiteSpace(expectedNonce)
            && !string.Equals(registered.Nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new UnauthorizedException($"{provider} nonce validation failed");
        }

        return Task.FromResult(registered.User);
    }
}
