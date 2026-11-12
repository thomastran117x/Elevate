using backend.main.features.auth.passkeys;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class PasskeyCeremonyStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> _values = [];
    private readonly Dictionary<string, HashSet<string>> _sets = [];
    private readonly Mock<ICacheService> _cache = new();
    private readonly MutableTime _time = new(Now);
    private bool _scriptsUnavailable;

    public PasskeyCeremonyStoreTests()
    {
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((key, value, _) => _values[key] = value)
            .ReturnsAsync(true);
        _cache.Setup(c => c.SetAddAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, member) =>
            {
                if (!_sets.TryGetValue(key, out var set))
                    _sets[key] = set = [];
                set.Add(member);
            })
            .ReturnsAsync(true);
        _cache.Setup(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        _cache.Setup(c => c.SetMembersAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _sets.TryGetValue(key, out var set) ? set.ToArray() : []);
        _cache.Setup(c => c.DeleteKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _values.Remove(key) | _sets.Remove(key));
        // The consume script: GET then DEL in one step.
        _cache.Setup(c => c.TryEvalAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>()))
            .ReturnsAsync((string _, RedisKey[] keys, RedisValue[] _) =>
            {
                if (_scriptsUnavailable)
                    return new CacheScriptResult(false, null);

                var key = keys[0].ToString();
                return _values.Remove(key, out var value)
                    ? new CacheScriptResult(true, value)
                    : new CacheScriptResult(true, null);
            });
    }

    [Fact]
    public async Task CreateAsync_ShouldStoreUnderAHashOfTheId_ForTheCeremonyTimeout()
    {
        string? storedKey = null;
        TimeSpan? storedTtl = null;
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((key, value, ttl) =>
            {
                storedKey = key;
                storedTtl = ttl;
                _values[key] = value;
            })
            .ReturnsAsync(true);

        var ceremony = Registration();
        var id = await Store().CreateAsync(ceremony);

        id.Should().NotBeNullOrWhiteSpace();
        storedKey.Should().StartWith("auth:passkey:ceremony:").And.NotContain(id);
        storedTtl.Should().Be(TimeSpan.FromSeconds(120));
        ceremony.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddSeconds(120));
        _sets["auth:passkey:ceremony-index:user:7"].Should().Contain(storedKey!);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotIndexASignInCeremony_WhichBelongsToNoAccount()
    {
        await Store().CreateAsync(new PasskeyCeremony { Type = PasskeyCeremonyType.SignIn, OptionsJson = "{}" });

        _sets.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_WhenTheCacheCannotStoreIt()
    {
        _cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);

        var act = () => Store().CreateAsync(Registration());

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task ConsumeAsync_ShouldReturnTheCeremonyOnce()
    {
        var store = Store();
        var id = await store.CreateAsync(Registration());

        var first = await store.ConsumeAsync(id, PasskeyCeremonyType.Registration);
        var second = await store.ConsumeAsync(id, PasskeyCeremonyType.Registration);

        first!.UserId.Should().Be(7);
        first.SessionId.Should().Be("sid-1");
        first.FriendlyName.Should().Be("Laptop");
        second.Should().BeNull();
    }

    [Fact]
    public async Task ConsumeAsync_ShouldSpendACeremonyPresentedForAnotherType()
    {
        var store = Store();
        var id = await store.CreateAsync(Registration());

        (await store.ConsumeAsync(id, PasskeyCeremonyType.StepUp)).Should().BeNull();
        (await store.ConsumeAsync(id, PasskeyCeremonyType.Registration)).Should().BeNull();
    }

    [Fact]
    public async Task ConsumeAsync_ShouldRefuseAnExpiredCeremony_EvenIfTheKeyLingers()
    {
        var store = Store();
        var id = await store.CreateAsync(Registration());
        _time.Advance(TimeSpan.FromSeconds(121));

        (await store.ConsumeAsync(id, PasskeyCeremonyType.Registration)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown-id")]
    public async Task ConsumeAsync_ShouldReturnNull_ForAnUnknownId(string id)
    {
        (await Store().ConsumeAsync(id, PasskeyCeremonyType.SignIn)).Should().BeNull();
    }

    [Fact]
    public async Task ConsumeAsync_ShouldReturnNull_ForAStoredValueThatIsNotACeremony()
    {
        var store = Store();
        var id = await store.CreateAsync(Registration());
        foreach (var key in _values.Keys.ToList())
            _values[key] = "{not json";

        (await store.ConsumeAsync(id, PasskeyCeremonyType.Registration)).Should().BeNull();
    }

    [Fact]
    public async Task ConsumeAsync_ShouldThrow_WhenTheCacheCannotAnswer()
    {
        var store = Store();
        var id = await store.CreateAsync(Registration());
        _scriptsUnavailable = true;

        var act = () => store.ConsumeAsync(id, PasskeyCeremonyType.Registration);

        await act.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task RevokeAllForUserAsync_ShouldCancelEveryCeremonyTheAccountHasInFlight()
    {
        var store = Store();
        var first = await store.CreateAsync(Registration());
        var second = await store.CreateAsync(Registration(PasskeyCeremonyType.StepUp));

        await store.RevokeAllForUserAsync(7);

        (await store.ConsumeAsync(first, PasskeyCeremonyType.Registration)).Should().BeNull();
        (await store.ConsumeAsync(second, PasskeyCeremonyType.StepUp)).Should().BeNull();
        _sets.Should().BeEmpty();
    }

    private PasskeyCeremonyStore Store() =>
        new(_cache.Object, _time, Options.Create(new PasskeyOptions { CeremonyTimeoutSeconds = 120 }));

    private static PasskeyCeremony Registration(PasskeyCeremonyType type = PasskeyCeremonyType.Registration) => new()
    {
        Type = type,
        OptionsJson = "{\"challenge\":\"abc\"}",
        UserId = 7,
        SessionId = "sid-1",
        AuthVersion = 3,
        FriendlyName = "Laptop",
    };

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
