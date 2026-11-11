using backend.main.features.auth.oauth.links;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

using Newtonsoft.Json;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Auth.ProviderLinks;

public class ProviderLinkStateStoreTests
{
    private readonly Mock<ICacheService> _cache = new() { CallBase = true };

    public ProviderLinkStateStoreTests()
    {
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        _cache.Setup(c => c.SetAddAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _cache.Setup(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task CreateLinkStateAsync_ShouldStoreUnderAHashOfTheToken_ForTheProofWindow()
    {
        string? storedKey = null;
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((key, _, _) => storedKey = key)
            .ReturnsAsync(true);

        var token = await new ProviderLinkStateStore(_cache.Object).CreateLinkStateAsync(State());

        token.Should().NotBeNullOrWhiteSpace();
        storedKey.Should().StartWith("oauth:link-state:").And.NotContain(token);
        _cache.Verify(c => c.SetValueAsync(storedKey!, It.IsAny<string>(), ProviderLinkStateLifetime.Lifetime), Times.Once);
        _cache.Verify(c => c.SetAddAsync("oauth:link-index:user:7", storedKey!), Times.Once);
    }

    [Fact]
    public async Task CreateLinkStateAsync_ShouldFail_WhenTheCacheCannotStoreIt()
    {
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);

        var act = () => new ProviderLinkStateStore(_cache.Object).CreateLinkStateAsync(State());

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task ConsumeLinkStateAsync_ShouldReturnTheStoredState()
    {
        _cache.Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)JsonConvert.SerializeObject(State())));

        var state = await new ProviderLinkStateStore(_cache.Object).ConsumeLinkStateAsync("token");

        state!.UserId.Should().Be(7);
        state.Nonce.Should().Be("nonce");
    }

    [Fact]
    public async Task ConsumeLinkStateAsync_ShouldReturnNull_ForAStateThatIsGone()
    {
        _cache.Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync(RedisResult.Create(RedisValue.Null));
        var store = new ProviderLinkStateStore(_cache.Object);

        (await store.ConsumeLinkStateAsync("token")).Should().BeNull();
        (await store.ConsumeLinkStateAsync("")).Should().BeNull();
    }

    /// <summary>An unreachable cache must never read as an absent state, nor as a present one.</summary>
    [Fact]
    public async Task ConsumeLinkStateAsync_ShouldFail_WhenTheCacheIsUnavailable()
    {
        _cache.Setup(c => c.EvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var act = () => new ProviderLinkStateStore(_cache.Object).ConsumePendingEmailLinkAsync("token");

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task RevokeAllForUserAsync_ShouldDeleteEveryOutstandingEntryAndTheIndex()
    {
        _cache.Setup(c => c.SetMembersAsync("oauth:link-index:user:7"))
            .ReturnsAsync(["oauth:link-state:A", "oauth:pending-link:B"]);

        await new ProviderLinkStateStore(_cache.Object).RevokeAllForUserAsync(7);

        _cache.Verify(c => c.DeleteKeyAsync("oauth:link-state:A"), Times.Once);
        _cache.Verify(c => c.DeleteKeyAsync("oauth:pending-link:B"), Times.Once);
        _cache.Verify(c => c.DeleteKeyAsync("oauth:link-index:user:7"), Times.Once);
    }

    private static ProviderLinkState State() => new()
    {
        Provider = IdentityProviders.Google,
        UserId = 7,
        SessionId = "session-7",
        AuthVersion = 1,
        Nonce = "nonce",
        CodeVerifier = "verifier",
        RedirectUri = "https://app.example/auth/google",
        CreatedAtUtc = DateTime.UtcNow,
    };
}
