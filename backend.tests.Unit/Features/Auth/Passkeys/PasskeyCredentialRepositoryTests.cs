using System.Security.Cryptography;

using backend.main.features.auth.oauth.links;
using backend.main.features.auth.passkeys;
using backend.tests.Unit.Features.Auth.ProviderLinks;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class PasskeyCredentialRepositoryTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AddAsync_ShouldStoreThePasskey_AndListItForItsAccountOnly()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);

        (await repository.AddAsync(NewPasskey(owner, "first"), maxPerUser: 5)).Should().Be(PasskeyAddResult.Added);
        (await repository.AddAsync(NewPasskey(owner, "second", Now.AddMinutes(1)), maxPerUser: 5)).Should().Be(PasskeyAddResult.Added);
        await repository.AddAsync(NewPasskey(other, "theirs"), maxPerUser: 5);

        var listed = await repository.ListForUserAsync(owner);
        listed.Select(p => p.FriendlyName).Should().Equal("first", "second");
        (await repository.GetFactorSummaryAsync(owner)).Should().Be(new PasskeyFactorSummary(2, Now.AddMinutes(1)));
        (await repository.GetFactorSummaryAsync(9999)).Should().Be(new PasskeyFactorSummary(0, null));
    }

    /// <summary>A credential id belongs to one account: a second registration, by anyone, is refused.</summary>
    [Fact]
    public async Task AddAsync_ShouldReportCredentialTaken_WhenAnyAccountHoldsTheCredentialId()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "mine");
        await repository.AddAsync(passkey, maxPerUser: 5);

        var copy = NewPasskey(other, "stolen");
        copy.CredentialId = passkey.CredentialId;
        (await repository.AddAsync(copy, maxPerUser: 5)).Should().Be(PasskeyAddResult.CredentialTaken);

        var found = await repository.FindByCredentialIdAsync(passkey.CredentialId);
        found!.UserId.Should().Be(owner);
        (await repository.ListForUserAsync(other)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_ShouldRefuse_OnceTheAccountHoldsTheLimit()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        await repository.AddAsync(NewPasskey(owner, "one"), maxPerUser: 1);

        (await repository.AddAsync(NewPasskey(owner, "two"), maxPerUser: 1)).Should().Be(PasskeyAddResult.LimitReached);
        (await repository.ListForUserAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_ShouldReportAMissingAccount()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var repository = new PasskeyCredentialRepository(harness.Db);

        (await repository.AddAsync(NewPasskey(424242, "orphan"), maxPerUser: 5)).Should().Be(PasskeyAddResult.UserNotFound);
    }

    [Fact]
    public async Task GetOrCreateUserHandleAsync_ShouldCreateOneRandomHandle_AndKeepIt()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);

        var first = await repository.GetOrCreateUserHandleAsync(owner);
        var again = await repository.GetOrCreateUserHandleAsync(owner);
        var theirs = await repository.GetOrCreateUserHandleAsync(other);

        first.Should().HaveCount(PasskeyCredentialRepository.UserHandleBytes);
        again.Should().Equal(first);
        theirs.Should().NotEqual(first);
        (await repository.GetOrCreateUserHandleAsync(9999)).Should().BeNull();
    }

    [Fact]
    public async Task RenameAsync_ShouldOnlyRenameTheAccountsOwnPasskey()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "old");
        await repository.AddAsync(passkey, maxPerUser: 5);

        (await repository.RenameAsync(other, passkey.Id, "hijacked")).Should().BeFalse();
        (await repository.RenameAsync(owner, passkey.Id, "new")).Should().BeTrue();

        (await repository.ListForUserAsync(owner)).Single().FriendlyName.Should().Be("new");
    }

    [Fact]
    public async Task RecordUseAsync_ShouldOnlyMoveTheCounterForward_AndRecordTheUse()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "key");
        passkey.SignatureCounter = 10;
        await repository.AddAsync(passkey, maxPerUser: 5);

        await repository.RecordUseAsync(passkey.Id, 4, isBackedUp: true, Now.AddHours(1), counterAnomaly: true);

        var stored = await repository.FindByCredentialIdAsync(passkey.CredentialId);
        stored!.SignatureCounter.Should().Be(10);
        stored.IsBackedUp.Should().BeTrue();
        stored.LastUsedAtUtc.Should().Be(Now.AddHours(1));
        stored.CounterAnomalyAtUtc.Should().Be(Now.AddHours(1));

        await repository.RecordUseAsync(passkey.Id, 11, isBackedUp: false, Now.AddHours(2), counterAnomaly: false);

        stored = await repository.FindByCredentialIdAsync(passkey.CredentialId);
        stored!.SignatureCounter.Should().Be(11);
        stored.CounterAnomalyAtUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task FlagCounterAnomalyAsync_ShouldNotCountAsAUse()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "key");
        await repository.AddAsync(passkey, maxPerUser: 5);

        await repository.FlagCounterAnomalyAsync(passkey.Id, Now);

        var stored = await repository.FindByCredentialIdAsync(passkey.CredentialId);
        stored!.CounterAnomalyAtUtc.Should().Be(Now);
        stored.LastUsedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RemoveIfAnotherMethodRemainsAsync_ShouldRemove_WhenThePasswordRemains()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "only");
        await repository.AddAsync(passkey, maxPerUser: 5);

        (await repository.RemoveIfAnotherMethodRemainsAsync(owner, passkey.Id)).Should().Be(PasskeyRemoveResult.Removed);
        (await repository.ListForUserAsync(owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveIfAnotherMethodRemainsAsync_ShouldRefuseTheLastSignInMethod()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("passwordless@example.com", password: null);
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "only");
        await repository.AddAsync(passkey, maxPerUser: 5);

        (await repository.RemoveIfAnotherMethodRemainsAsync(owner, passkey.Id))
            .Should().Be(PasskeyRemoveResult.LastSignInMethod);
        (await repository.ListForUserAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task RemoveIfAnotherMethodRemainsAsync_ShouldCountAProviderLinkOrAnotherPasskeyAsRemaining()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var linked = await harness.SeedUserAsync("linked@example.com", password: null);
        var twoKeys = await harness.SeedUserAsync("twokeys@example.com", password: null);
        await harness.Repository.AddAsync(new UserIdentityLink
        {
            UserId = linked,
            Provider = IdentityProviders.Google,
            Subject = "google-1",
            LinkedAtUtc = Now,
        });
        var repository = new PasskeyCredentialRepository(harness.Db);
        var linkedKey = NewPasskey(linked, "key");
        var firstKey = NewPasskey(twoKeys, "first");
        var secondKey = NewPasskey(twoKeys, "second");
        await repository.AddAsync(linkedKey, maxPerUser: 5);
        await repository.AddAsync(firstKey, maxPerUser: 5);
        await repository.AddAsync(secondKey, maxPerUser: 5);

        (await repository.RemoveIfAnotherMethodRemainsAsync(linked, linkedKey.Id)).Should().Be(PasskeyRemoveResult.Removed);
        (await repository.RemoveIfAnotherMethodRemainsAsync(twoKeys, firstKey.Id)).Should().Be(PasskeyRemoveResult.Removed);
        (await repository.RemoveIfAnotherMethodRemainsAsync(twoKeys, secondKey.Id))
            .Should().Be(PasskeyRemoveResult.LastSignInMethod);
    }

    [Fact]
    public async Task RemoveIfAnotherMethodRemainsAsync_ShouldNotRemoveAnotherAccountsPasskey()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        var passkey = NewPasskey(owner, "mine");
        await repository.AddAsync(passkey, maxPerUser: 5);

        (await repository.RemoveIfAnotherMethodRemainsAsync(other, passkey.Id)).Should().Be(PasskeyRemoveResult.NotFound);
        (await repository.ListForUserAsync(owner)).Should().ContainSingle();
    }

    /// <summary>Unlinking a provider now counts passkeys as a remaining way to sign in.</summary>
    [Fact]
    public async Task ProviderUnlink_ShouldSucceed_WhenOnlyAPasskeyRemains()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com", password: null);
        await harness.Repository.AddAsync(new UserIdentityLink
        {
            UserId = owner,
            Provider = IdentityProviders.Google,
            Subject = "google-1",
            LinkedAtUtc = Now,
        });
        var repository = new PasskeyCredentialRepository(harness.Db);
        await repository.AddAsync(NewPasskey(owner, "key"), maxPerUser: 5);

        (await harness.Repository.RemoveIfAnotherMethodRemainsAsync(owner, IdentityProviders.Google))
            .Should().Be(IdentityLinkRemoveResult.Removed);
    }

    [Fact]
    public async Task DeletingTheAccount_ShouldCascadeToItsPasskeys()
    {
        await using var harness = await UserIdentityLinkRepositoryTests.LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var repository = new PasskeyCredentialRepository(harness.Db);
        await repository.AddAsync(NewPasskey(owner, "key"), maxPerUser: 5);

        await harness.Db.Users.Where(u => u.Id == owner).ExecuteDeleteAsync();

        (await harness.Db.PasskeyCredentials.CountAsync()).Should().Be(0);
    }

    private static PasskeyCredential NewPasskey(int userId, string name, DateTime? createdAtUtc = null) => new()
    {
        UserId = userId,
        CredentialId = RandomNumberGenerator.GetBytes(16),
        PublicKey = RandomNumberGenerator.GetBytes(77),
        UserHandle = RandomNumberGenerator.GetBytes(32),
        FriendlyName = name,
        CreatedAtUtc = createdAtUtc ?? Now,
    };
}
