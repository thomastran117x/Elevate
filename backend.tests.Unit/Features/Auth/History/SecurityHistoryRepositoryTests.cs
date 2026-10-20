using backend.main.features.auth.history;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPageAsync_ShouldReturnOnlyTheAccountsUnexpiredEntries_NewestFirst()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        await harness.SeedAsync(
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.AddHours(-3), AccountSecurityEventKind.PasswordChanged),
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.AddHours(-1), AccountSecurityEventKind.SignedIn),
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.AddDays(-91), AccountSecurityEventKind.MfaEnabled, expiresAt: Now.AddDays(-1)),
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OtherId, Now.AddMinutes(-1)));

        var repository = new SecurityHistoryRepository(harness.Db);
        var (items, total) = await repository.GetPageAsync(SecurityHistoryDbHarness.OwnerId, Now, 1, 20);

        total.Should().Be(2);
        items.Select(i => i.Kind).Should().Equal(
            AccountSecurityEventKind.SignedIn,
            AccountSecurityEventKind.PasswordChanged);
    }

    [Fact]
    public async Task GetPageAsync_ShouldBreakTimestampTiesById_AndSkipEarlierPages()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        await harness.SeedAsync(Enumerable.Range(0, 5)
            .Select(_ => SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.AddHours(-1)))
            .ToArray());

        var repository = new SecurityHistoryRepository(harness.Db);
        var (first, total) = await repository.GetPageAsync(SecurityHistoryDbHarness.OwnerId, Now, 1, 2);
        var (second, _) = await repository.GetPageAsync(SecurityHistoryDbHarness.OwnerId, Now, 2, 2);
        var (third, _) = await repository.GetPageAsync(SecurityHistoryDbHarness.OwnerId, Now, 3, 2);

        total.Should().Be(5);
        first.Concat(second).Concat(third).Select(i => i.Id)
            .Should().HaveCount(5).And.OnlyHaveUniqueItems().And.BeInDescendingOrder();
    }

    [Fact]
    public async Task GetPageAsync_ShouldReturnNothing_WhenTheAccountHasNoHistory()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();

        var repository = new SecurityHistoryRepository(harness.Db);
        var (items, total) = await repository.GetPageAsync(SecurityHistoryDbHarness.OwnerId, Now, 1, 20);

        total.Should().Be(0);
        items.Should().BeEmpty();
    }
}
