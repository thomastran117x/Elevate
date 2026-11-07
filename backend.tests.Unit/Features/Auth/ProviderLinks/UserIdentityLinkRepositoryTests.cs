using backend.main.features.auth.oauth.links;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth.ProviderLinks;

public class UserIdentityLinkRepositoryTests
{
    [Fact]
    public async Task AddAsync_ShouldStoreTheLink()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");

        var result = await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-1"));

        result.Should().Be(IdentityLinkAddResult.Added);
        var stored = await harness.Repository.FindBySubjectAsync(IdentityProviders.Google, "google-1");
        stored!.UserId.Should().Be(userId);
        stored.Email.Should().Be("provider@example.com");
    }

    /// <summary>A provider subject maps to at most one account, whichever asks.</summary>
    [Fact]
    public async Task AddAsync_ShouldReportSubjectTaken_WhenAnotherAccountHoldsTheSubject()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var owner = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        await harness.Repository.AddAsync(NewLink(owner, IdentityProviders.Microsoft, "ms-1"));

        var result = await harness.Repository.AddAsync(NewLink(other, IdentityProviders.Microsoft, "ms-1"));

        result.Should().Be(IdentityLinkAddResult.SubjectTaken);
        (await harness.Repository.FindBySubjectAsync(IdentityProviders.Microsoft, "ms-1"))!
            .UserId.Should().Be(owner);
        (await harness.Repository.ListForUserAsync(other)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_ShouldReportProviderTaken_WhenTheAccountLinksAnotherSubjectForTheProvider()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-1"));

        var result = await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-2"));

        result.Should().Be(IdentityLinkAddResult.ProviderTaken);
        (await harness.Repository.FindBySubjectAsync(IdentityProviders.Google, "google-2")).Should().BeNull();
    }

    /// <summary>The same subject string under two providers is two different identities.</summary>
    [Fact]
    public async Task AddAsync_ShouldScopeSubjectsByProvider()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var first = await harness.SeedUserAsync("first@example.com");
        var second = await harness.SeedUserAsync("second@example.com");
        await harness.Repository.AddAsync(NewLink(first, IdentityProviders.Google, "same-subject"));

        var result = await harness.Repository.AddAsync(NewLink(second, IdentityProviders.Microsoft, "same-subject"));

        result.Should().Be(IdentityLinkAddResult.Added);
    }

    [Fact]
    public async Task ListForUserAsync_ShouldReturnOnlyTheAccountsLinks_InProviderOrder()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");
        var other = await harness.SeedUserAsync("other@example.com");
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Microsoft, "ms-1"));
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-1"));
        await harness.Repository.AddAsync(NewLink(other, IdentityProviders.Google, "google-other"));

        var links = await harness.Repository.ListForUserAsync(userId);

        links.Select(l => l.Provider).Should().Equal(IdentityProviders.Google, IdentityProviders.Microsoft);
    }

    [Fact]
    public async Task TouchAsync_ShouldRecordUseAndRefreshMetadata()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-1"));
        var link = await harness.Repository.FindBySubjectAsync(IdentityProviders.Google, "google-1");
        var usedAt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        await harness.Repository.TouchAsync(link!.Id, "renamed@example.com", "Renamed", usedAt);

        var stored = await harness.Repository.FindBySubjectAsync(IdentityProviders.Google, "google-1");
        stored!.LastUsedAtUtc.Should().Be(usedAt);
        stored.Email.Should().Be("renamed@example.com");
        stored.DisplayName.Should().Be("Renamed");
        stored.Subject.Should().Be("google-1");
    }

    /// <summary>A provider that omits a claim on one sign-in has not reported that it changed.</summary>
    [Fact]
    public async Task TouchAsync_ShouldKeepStoredMetadata_WhenTheProviderReportsNone()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Microsoft, "ms-1"));
        var link = await harness.Repository.FindBySubjectAsync(IdentityProviders.Microsoft, "ms-1");

        await harness.Repository.TouchAsync(link!.Id, null, "  ", DateTime.UtcNow);

        var stored = await harness.Repository.FindBySubjectAsync(IdentityProviders.Microsoft, "ms-1");
        stored!.Email.Should().Be("provider@example.com");
        stored.DisplayName.Should().Be("Provider Name");
        stored.LastUsedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DeletingTheAccount_ShouldCascadeToItsLinks()
    {
        await using var harness = await LinkRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync("owner@example.com");
        await harness.Repository.AddAsync(NewLink(userId, IdentityProviders.Google, "google-1"));

        await harness.Db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();

        (await harness.Db.UserIdentityLinks.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    private static UserIdentityLink NewLink(int userId, string provider, string subject) => new()
    {
        UserId = userId,
        Provider = provider,
        Subject = subject,
        Email = "provider@example.com",
        DisplayName = "Provider Name",
        LinkedAtUtc = DateTime.UtcNow,
    };

    internal sealed class LinkRepositoryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public UserIdentityLinkRepository Repository { get; }

        private LinkRepositoryHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new UserIdentityLinkRepository(db);
        }

        public static async Task<LinkRepositoryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            // SQLite enforces foreign keys, and so the cascade, only when asked.
            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                await pragma.ExecuteNonQueryAsync();
            }

            var db = new AppDatabaseContext(
                new DbContextOptionsBuilder<AppDatabaseContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            return new LinkRepositoryHarness(connection, db);
        }

        public async Task<int> SeedUserAsync(string email, string? password = "seed-password")
        {
            var user = new User
            {
                Email = email,
                Password = password,
                Usertype = "participant",
                Username = email.Split('@')[0],
            };

            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return user.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
