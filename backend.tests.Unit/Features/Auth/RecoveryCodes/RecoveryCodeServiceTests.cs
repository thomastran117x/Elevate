using backend.main.features.auth;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.RecoveryCodes;

public class RecoveryCodeServiceTests
{
    private const int UserId = 7;

    private readonly Mock<IRecoveryCodeRepository> _repository = new();
    private readonly Mock<IAuthUserRepository> _users = new();
    private readonly RecoveryCodeHasher _hasher = RecoveryCodeHasherTests.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly RecoveryCodeService _service;

    public RecoveryCodeServiceTests()
    {
        _users.Setup(u => u.GetUserAsync(UserId)).ReturnsAsync(new User
        {
            Id = UserId,
            Email = "member@example.com",
            Usertype = "Participant",
        });
        _service = new RecoveryCodeService(_repository.Object, _users.Object, _hasher, _time);
    }

    [Fact]
    public async Task RegenerateAsync_ShouldReturnTenDisplayCodes_AndStoreOnlyTheirHashes()
    {
        IReadOnlyList<RecoveryCode>? stored = null;
        _repository
            .Setup(r => r.ReplaceSetAsync(UserId, It.IsAny<IReadOnlyList<RecoveryCode>>()))
            .Callback<int, IReadOnlyList<RecoveryCode>>((_, rows) => stored = rows)
            .ReturnsAsync(true);

        var set = await _service.RegenerateAsync(UserId);

        set.Codes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        set.Replaced.Should().BeTrue();
        set.GeneratedAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
        stored.Should().NotBeNull().And.HaveCount(10);
        foreach (var (code, row) in set.Codes.Zip(stored!))
        {
            RecoveryCodeFormat.TryNormalize(code, out var normalized).Should().BeTrue();
            row.CodeHash.Should().StartWith("$argon2id$").And.NotContain(normalized);
            row.Selector.Should().Be(_hasher.Selector(UserId, normalized));
            _hasher.Verify(UserId, normalized, row.CodeHash).Should().BeTrue();
            row.CreatedAtUtc.Should().Be(set.GeneratedAtUtc);
            row.UsedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task TryConsumeAsync_ShouldSpendAMatchingUnusedCode()
    {
        var code = Seed(id: 3);
        _repository.Setup(r => r.TryConsumeAsync(3, UserId, It.IsAny<DateTime>())).ReturnsAsync(true);

        (await _service.TryConsumeAsync(UserId, RecoveryCodeFormat.Display(code).ToLowerInvariant()))
            .Should().BeTrue();

        _repository.Verify(r => r.TryConsumeAsync(3, UserId, _time.GetUtcNow().UtcDateTime), Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_ShouldFail_WhenTheCodeWasSpentConcurrently()
    {
        var code = Seed(id: 3);
        _repository.Setup(r => r.TryConsumeAsync(3, UserId, It.IsAny<DateTime>())).ReturnsAsync(false);

        (await _service.TryConsumeAsync(UserId, code)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsumeAsync_ShouldFail_ForAnUnknownUsedOrReplacedCode()
    {
        // The repository returns only unused codes of the current set, so a used or replaced code
        // looks exactly like an unknown one.
        (await _service.TryConsumeAsync(UserId, RecoveryCodeFormat.GenerateSet()[0])).Should().BeFalse();

        _repository.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_ShouldFail_WhenTheSelectorMatchesButTheHashDoesNot()
    {
        var code = RecoveryCodeFormat.GenerateSet()[0];
        _repository
            .Setup(r => r.FindUnusedAsync(UserId, _hasher.Selector(UserId, code)))
            .ReturnsAsync(new RecoveryCode
            {
                Id = 3,
                UserId = UserId,
                Selector = _hasher.Selector(UserId, code),
                CodeHash = _hasher.Hash(UserId, RecoveryCodeFormat.GenerateSet()[0]),
            });

        (await _service.TryConsumeAsync(UserId, code)).Should().BeFalse();

        _repository.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_ShouldFail_ForAnotherAccountsCode()
    {
        var code = RecoveryCodeFormat.GenerateSet()[0];
        // Another account's row: even handed to this account, it neither matches its selector
        // nor verifies under its associated data.
        _repository
            .Setup(r => r.FindUnusedAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync(new RecoveryCode
            {
                Id = 9,
                UserId = 99,
                Selector = _hasher.Selector(99, code),
                CodeHash = _hasher.Hash(99, code),
            });

        (await _service.TryConsumeAsync(UserId, code)).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryConsumeAsync_ShouldFail_ForADisabledOrMissingAccount_WithoutSpending(bool disabled)
    {
        var code = Seed(id: 3);
        _users.Setup(u => u.GetUserAsync(UserId)).ReturnsAsync(disabled
            ? new User { Id = UserId, Email = "member@example.com", Usertype = "Participant", IsDisabled = true }
            : null);

        (await _service.TryConsumeAsync(UserId, code)).Should().BeFalse();

        _repository.Verify(r => r.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("not-a-recovery-code")]
    public async Task TryConsumeAsync_ShouldRejectMalformedInput_BeforeAnyLookup(string code)
    {
        var act = () => _service.TryConsumeAsync(UserId, code);

        await act.Should().ThrowAsync<BadRequestException>();
        _users.Verify(u => u.GetUserAsync(It.IsAny<int>()), Times.Never);
        _repository.Verify(r => r.FindUnusedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Failures_ShouldSurfaceAsInternalServerErrors()
    {
        _repository.Setup(r => r.GetStatusAsync(UserId)).ThrowsAsync(new InvalidOperationException("db"));
        _repository.Setup(r => r.HasUnusedAsync(UserId)).ThrowsAsync(new InvalidOperationException("db"));
        _repository
            .Setup(r => r.ReplaceSetAsync(UserId, It.IsAny<IReadOnlyList<RecoveryCode>>()))
            .ThrowsAsync(new InvalidOperationException("db"));
        _repository.Setup(r => r.FindUnusedAsync(UserId, It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("db"));

        await FluentActions.Awaiting(() => _service.GetStatusAsync(UserId)).Should().ThrowAsync<InternalServerErrorException>();
        await FluentActions.Awaiting(() => _service.HasUnusedAsync(UserId)).Should().ThrowAsync<InternalServerErrorException>();
        await FluentActions.Awaiting(() => _service.RegenerateAsync(UserId)).Should().ThrowAsync<InternalServerErrorException>();
        await FluentActions.Awaiting(() => _service.TryConsumeAsync(UserId, RecoveryCodeFormat.GenerateSet()[0]))
            .Should().ThrowAsync<InternalServerErrorException>();
    }

    [Fact]
    public async Task GetStatusAsync_AndHasUnusedAsync_ShouldReadTheRepository()
    {
        var status = new RecoveryCodeStatus(4, _time.GetUtcNow().UtcDateTime, null);
        _repository.Setup(r => r.GetStatusAsync(UserId)).ReturnsAsync(status);
        _repository.Setup(r => r.HasUnusedAsync(UserId)).ReturnsAsync(true);

        (await _service.GetStatusAsync(UserId)).Should().Be(status);
        (await _service.HasUnusedAsync(UserId)).Should().BeTrue();
    }

    private string Seed(int id)
    {
        var code = RecoveryCodeFormat.GenerateSet()[0];
        var selector = _hasher.Selector(UserId, code);
        _repository
            .Setup(r => r.FindUnusedAsync(UserId, selector))
            .ReturnsAsync(new RecoveryCode
            {
                Id = id,
                UserId = UserId,
                Selector = selector,
                CodeHash = _hasher.Hash(UserId, code),
            });
        return code;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
