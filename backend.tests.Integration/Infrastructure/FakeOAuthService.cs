using backend.main.features.auth.oauth;
using backend.main.shared.exceptions.http;

namespace backend.tests.Integration.Infrastructure;

public sealed class FakeOAuthService : IOAuthService
{
    private readonly Dictionary<string, OAuthUser> _googleTokens = [];
    private readonly Dictionary<string, OAuthUser> _microsoftTokens = [];

    public void RegisterGoogleToken(string token, OAuthUser user) => _googleTokens[token] = user;

    public void RegisterMicrosoftToken(string token, OAuthUser user) => _microsoftTokens[token] = user;

    public void Clear()
    {
        _googleTokens.Clear();
        _microsoftTokens.Clear();
    }

    public Task<string> ExchangeGoogleCodeAsync(string code, string codeVerifier, string redirectUri)
    {
        return Task.FromResult(code);
    }

    public Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken, string? expectedNonce = null)
    {
        return _googleTokens.TryGetValue(googleToken, out var user)
            ? Task.FromResult(user)
            : throw new UnauthorizedException("Invalid Google Token");
    }

    public Task<OAuthUser> VerifyMicrosoftTokenAsync(string microsoftToken, string? expectedNonce = null)
    {
        return _microsoftTokens.TryGetValue(microsoftToken, out var user)
            ? Task.FromResult(user)
            : throw new UnauthorizedException("Invalid Microsoft Token");
    }

    public Task<OAuthUser> VerifyAppleTokenAsync(string appleToken)
    {
        throw new NotSupportedException("Apple OAuth is not used in tests.");
    }
}
