using System.Net;

using backend.main.features.cache;
using backend.main.infrastructure.redis;
using backend.main.shared.utilities.logger;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Cache;

public class CacheServiceTests
{
    [Fact]
    public async Task StringAndCounterOperations_ShouldDelegateToRedis()
    {
        var harness = new CacheServiceHarness();
        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                false,
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.StringGetAsync("token", CommandFlags.None))
            .ReturnsAsync((RedisValue)"value");
        harness.DatabaseMock
            .Setup(db => db.StringIncrementAsync("visits", 2, CommandFlags.None))
            .ReturnsAsync(9);
        harness.DatabaseMock
            .Setup(db => db.StringDecrementAsync("credits", 3, CommandFlags.None))
            .ReturnsAsync(4);
        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var set = await harness.Service.SetValueAsync("token", "value", TimeSpan.FromMinutes(5));
        var get = await harness.Service.GetValueAsync("token");
        var increment = await harness.Service.IncrementAsync("visits", 2);
        var decrement = await harness.Service.DecrementAsync("credits", 3);
        var locked = await harness.Service.AcquireLockAsync("lock", "holder", TimeSpan.FromSeconds(30));

        set.Should().BeTrue();
        get.Should().Be("value");
        increment.Should().Be(9);
        decrement.Should().Be(4);
        locked.Should().BeTrue();
    }

    [Fact]
    public async Task HashAndSetOperations_ShouldMapRedisValues()
    {
        var harness = new CacheServiceHarness();
        harness.DatabaseMock
            .Setup(db => db.HashSetAsync("profile", "name", "Ada", When.Always, CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.HashGetAsync("profile", "name", CommandFlags.None))
            .ReturnsAsync((RedisValue)"Ada");
        harness.DatabaseMock
            .Setup(db => db.HashGetAllAsync("profile", CommandFlags.None))
            .ReturnsAsync(
            [
                new HashEntry("name", "Ada"),
                new HashEntry("role", "Organizer")
            ]);
        harness.DatabaseMock
            .Setup(db => db.HashDeleteAsync("profile", "role", CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.SetAddAsync("members", "ada", CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.SetRemoveAsync("members", "grace", CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.SetMembersAsync("members", CommandFlags.None))
            .ReturnsAsync([(RedisValue)"ada", (RedisValue)"grace"]);

        var hashSet = await harness.Service.HashSetAsync("profile", "name", "Ada");
        var hashGet = await harness.Service.HashGetAsync("profile", "name");
        var hashGetAll = await harness.Service.HashGetAllAsync("profile");
        var hashDelete = await harness.Service.HashDeleteAsync("profile", "role");
        var setAdd = await harness.Service.SetAddAsync("members", "ada");
        var setRemove = await harness.Service.SetRemoveAsync("members", "grace");
        var members = await harness.Service.SetMembersAsync("members");

        hashSet.Should().BeTrue();
        hashGet.Should().Be("Ada");
        hashGetAll.Should().Equal(new Dictionary<string, string>
        {
            ["name"] = "Ada",
            ["role"] = "Organizer"
        });
        hashDelete.Should().BeTrue();
        setAdd.Should().BeTrue();
        setRemove.Should().BeTrue();
        members.Should().Equal("ada", "grace");
    }

    [Fact]
    public async Task ListAndKeyOperations_ShouldDelegateToRedis()
    {
        var harness = new CacheServiceHarness();
        harness.DatabaseMock
            .Setup(db => db.ListLeftPushAsync("queue", "a", When.Always, CommandFlags.None))
            .ReturnsAsync(2);
        harness.DatabaseMock
            .Setup(db => db.ListRightPushAsync("queue", "b", When.Always, CommandFlags.None))
            .ReturnsAsync(3);
        harness.DatabaseMock
            .Setup(db => db.ListLeftPopAsync("queue", CommandFlags.None))
            .ReturnsAsync((RedisValue)"left");
        harness.DatabaseMock
            .Setup(db => db.ListRightPopAsync("queue", CommandFlags.None))
            .ReturnsAsync((RedisValue)"right");
        harness.DatabaseMock
            .Setup(db => db.KeyDeleteAsync("queue", CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.KeyExistsAsync("queue", CommandFlags.None))
            .ReturnsAsync(true);
        harness.DatabaseMock
            .Setup(db => db.KeyTimeToLiveAsync("queue", CommandFlags.None))
            .ReturnsAsync(TimeSpan.FromMinutes(1));
        harness.DatabaseMock
            .Setup(db => db.KeyExpireAsync("queue", TimeSpan.FromMinutes(2), ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        (await harness.Service.ListLeftPushAsync("queue", "a")).Should().Be(2);
        (await harness.Service.ListRightPushAsync("queue", "b")).Should().Be(3);
        (await harness.Service.ListLeftPopAsync("queue")).Should().Be("left");
        (await harness.Service.ListRightPopAsync("queue")).Should().Be("right");
        (await harness.Service.DeleteKeyAsync("queue")).Should().BeTrue();
        (await harness.Service.KeyExistsAsync("queue")).Should().BeTrue();
        (await harness.Service.GetTTLAsync("queue")).Should().Be(TimeSpan.FromMinutes(1));
        (await harness.Service.SetExpiryAsync("queue", TimeSpan.FromMinutes(2))).Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseLockAsync_ShouldDeleteKeyOnlyWhenValueMatches()
    {
        var harness = new CacheServiceHarness();
        harness.DatabaseMock
            .SetupSequence(db => db.StringGetAsync("lock", CommandFlags.None))
            .ReturnsAsync((RedisValue)"owner")
            .ReturnsAsync((RedisValue)"other");
        harness.DatabaseMock
            .Setup(db => db.KeyDeleteAsync("lock", CommandFlags.None))
            .ReturnsAsync(true);

        var released = await harness.Service.ReleaseLockAsync("lock", "owner");
        var blocked = await harness.Service.ReleaseLockAsync("lock", "owner");

        released.Should().BeTrue();
        blocked.Should().BeFalse();
        harness.DatabaseMock.Verify(db => db.KeyDeleteAsync("lock", CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task ServerAndBulkOperations_ShouldReturnExpectedValues()
    {
        var harness = new CacheServiceHarness();
        var server = new Mock<IServer>();
        var endpoint = new DnsEndPoint("localhost", 6379);

        harness.MultiplexerMock
            .Setup(redis => redis.GetEndPoints(It.IsAny<bool>()))
            .Returns([endpoint]);
        harness.MultiplexerMock
            .Setup(redis => redis.GetServer(endpoint, It.IsAny<object>()))
            .Returns(server.Object);
        server
            .Setup(item => item.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns([(RedisKey)"clubs:1", (RedisKey)"clubs:2"]);
        harness.DatabaseMock
            .Setup(db => db.StringGetAsync(It.Is<RedisKey[]>(keys => keys.Length == 2), CommandFlags.None))
            .ReturnsAsync([(RedisValue)"A", RedisValue.Null]);

        var resolvedServer = harness.Service.GetServer();
        var scanned = harness.Service.ScanKeys(server.Object, "clubs:*").ToArray();
        var many = await harness.Service.GetManyAsync(["first", "second"]);
        var empty = await harness.Service.GetManyAsync([]);

        resolvedServer.Should().BeSameAs(server.Object);
        scanned.Should().Equal("clubs:1", "clubs:2");
        many.Should().Equal(new Dictionary<string, string?>
        {
            ["first"] = "A",
            ["second"] = null
        });
        empty.Should().BeEmpty();
    }

    [Fact]
    public async Task EvalAsync_ShouldReturnRedisResult_OrFallbackArray()
    {
        var harness = new CacheServiceHarness();
        var keys = new RedisKey[] { "rate:1" };
        var values = new RedisValue[] { "60" };

        harness.DatabaseMock
            .SetupSequence(db => db.ScriptEvaluateAsync("return 1", keys, values, CommandFlags.None))
            .ReturnsAsync(RedisResult.Create((RedisValue)123))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var success = await harness.Service.EvalAsync("return 1", keys, values);
        var fallback = await harness.Service.EvalAsync("return 1", keys, values);

        ((int)(RedisResult)success).Should().Be(123);
        fallback.Should().BeEquivalentTo(new[] { 1, 0 });
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFallback_AndLog_ForRedisFailures()
    {
        var harness = new CacheServiceHarness();
        var logger = new RecordingLogger();
        Logger.SetInstance(logger);

        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                false,
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("timed out", CommandStatus.Unknown));
        harness.DatabaseMock
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("timed out", CommandStatus.Unknown));
        harness.DatabaseMock
            .Setup(db => db.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline", null, CommandStatus.Unknown));
        harness.DatabaseMock
            .Setup(db => db.KeyDeleteAsync("explode", CommandFlags.None))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var timedOut = await harness.Service.SetValueAsync("timeout", "value");
        var disconnected = await harness.Service.HashSetAsync("hash", "field", "value");
        var deleted = await harness.Service.DeleteKeyAsync("explode");

        timedOut.Should().BeFalse();
        disconnected.Should().BeFalse();
        deleted.Should().BeFalse();
        logger.Messages.Should().Contain(message => message.Contains("WarnEx:Redis timeout:RedisTimeoutException"));
        logger.Messages.Should().Contain(message => message.Contains("WarnEx:Redis connection error:RedisConnectionException"));
        logger.Messages.Should().Contain(message => message.Contains("ErrorEx:Unexpected Redis error:InvalidOperationException"));
    }

    private sealed class CacheServiceHarness
    {
        public Mock<IDatabase> DatabaseMock { get; } = new();
        public Mock<IConnectionMultiplexer> MultiplexerMock { get; } = new();
        public CacheService Service { get; }

        public CacheServiceHarness()
        {
            MultiplexerMock
                .Setup(redis => redis.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(DatabaseMock.Object);

            Service = new CacheService(new RedisResource(MultiplexerMock.Object));
        }
    }

    private sealed class RecordingLogger : ICustomLogger
    {
        public List<string> Messages { get; } = [];

        public void Debug(string message) => Messages.Add($"Debug:{message}");

        public void Info(string message) => Messages.Add($"Info:{message}");

        public void Warn(string message) => Messages.Add($"Warn:{message}");

        public void Error(string message) => Messages.Add($"Error:{message}");

        public void Warn(Exception ex, string? message = null) =>
            Messages.Add($"WarnEx:{message}:{ex.GetType().Name}");

        public void Error(Exception ex, string? message = null) =>
            Messages.Add($"ErrorEx:{message}:{ex.GetType().Name}");

        public void Log(LogLevel level, string message) =>
            Messages.Add($"{level}:{message}");

        public void Log(LogLevel level, Exception ex, string? message = null) =>
            Messages.Add($"{level}Ex:{message}:{ex.GetType().Name}");
    }
}
