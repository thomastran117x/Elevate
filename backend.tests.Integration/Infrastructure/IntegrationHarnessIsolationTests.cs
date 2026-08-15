using System.Net;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.oauth;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class IntegrationHarnessIsolationTests
{
    [Fact]
    public async Task TemplateClones_ShouldContainMigrations_ButNoApplicationRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PooledSlots_ShouldResetAndKeepConcurrentStateIsolated()
    {
        var firstLeases = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => AuthApiTestApp.CreateAsync()));
        var blobUrls = new Dictionary<int, string>();

        try
        {
            firstLeases.Select(app => app.ResourceSlot).Should().OnlyHaveUniqueItems();

            foreach (var app in firstLeases)
            {
                var signup = await app.PostJsonWithCsrfAsync("/api/auth/signup", new SignUpRequest
                {
                    Email = "old-notification@example.com",
                    Username = "old-notification",
                    Password = "Password123!",
                    Usertype = "Participant",
                    Captcha = "captcha"
                });
                signup.StatusCode.Should().Be(HttpStatusCode.OK);
                await app.SeedUserAsync("same-address@example.com");
                await app.Cache.SetValueAsync("same-key", $"slot-{app.ResourceSlot}");
                app.Captcha.ShouldSucceed = false;
                app.OAuth.RegisterGoogleToken(
                    "old-token",
                    new OAuthUser("old-id", "old@example.com", "Old User", "google"));
                blobUrls[app.ResourceSlot] =
                    app.BlobStorage.CreateOwnedBlobUrl("events/1", "old.png");
            }
        }
        finally
        {
            foreach (var app in firstLeases)
                await app.DisposeAsync();
        }

        var reusedLeases = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => AuthApiTestApp.CreateAsync()));

        try
        {
            reusedLeases.Select(app => app.ResourceSlot).Should().OnlyHaveUniqueItems();
            var oldEmails = await Task.WhenAll(
                reusedLeases.Select(app => app.ReadNewEmailMessagesAsync()));
            oldEmails.Should().OnlyContain(messages => messages.Count == 0);
            foreach (var app in reusedLeases)
            {
                (await app.FindUserByEmailAsync("same-address@example.com")).Should().BeNull();
                (await app.Cache.GetValueAsync("same-key")).Should().BeNull();
                app.Captcha.ShouldSucceed.Should().BeTrue();
                await FluentActions.Invoking(() => app.OAuth.VerifyGoogleTokenAsync("old-token"))
                    .Should().ThrowAsync<Exception>();
                app.BlobStorage.IsOwnedBlobUrl(blobUrls[app.ResourceSlot])
                    .Should().BeFalse();
            }
        }
        finally
        {
            foreach (var app in reusedLeases)
                await app.DisposeAsync();
        }
    }
}
