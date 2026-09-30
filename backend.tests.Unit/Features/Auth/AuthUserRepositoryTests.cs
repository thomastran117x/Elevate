using backend.main.features.auth;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace backend.tests.Unit.Features.Auth;

public class AuthUserRepositoryTests
{
    [Fact]
    public async Task CreateUserAsync_ShouldPersistUser_AndNormalizeRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "new@example.com",
            Password = "hashed-password",
            Usertype = "organizer",
            Name = "New User"
        });

        created.Id.Should().BeGreaterThan(0);
        created.Usertype.Should().Be("Organizer");

        var stored = await harness.Db.Users.SingleAsync(user => user.Email == "new@example.com");
        stored.Usertype.Should().Be("Organizer");
        stored.Name.Should().Be("New User");
    }

    /// <summary>
    /// Callers check availability outside the transaction, so two signups can both see a name as
    /// free and race to insert it. The loser must get the same conflict the pre-flight check
    /// produces, not the unique-index violation as a 500.
    /// </summary>
    [Fact]
    public async Task CreateUserAsync_ShouldReportAConflict_WhenItLosesTheRaceForAUsername()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(email: "holder@example.com", username: "contested-name");

        var act = () => harness.Repository.CreateUserAsync(new User
        {
            Email = "loser@example.com",
            Username = "contested-name",
            Password = "hashed-password",
            Usertype = "participant"
        });

        var exception = await act.Should().ThrowAsync<UsernameTakenException>();
        exception.Which.ErrorCode.Should().Be("USERNAME_TAKEN");
    }

    /// <summary>
    /// The Postgres branch of the detector matches on the index name, and no unit test can reach
    /// it because the harness runs SQLite. Pin the name against the model instead: if EF ever
    /// names the index differently, a real race would quietly go back to being a 500.
    /// </summary>
    [Fact]
    public async Task TheUsernameUniqueIndex_ShouldBeNamedAsTheConflictDetectorExpects()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var index = harness.Db.Model
            .FindEntityType(typeof(User))!
            .GetIndexes()
            .Single(candidate => candidate.IsUnique
                && candidate.Properties.Count == 1
                && candidate.Properties[0].Name == nameof(User.Username));

        index.GetDatabaseName().Should().Be("IX_Users_Username");
    }

    /// <summary>
    /// Narrow on purpose: the same insert can collide on Email, GoogleID or MicrosoftID, and
    /// reporting one of those as a taken username would send the caller to fix the wrong field.
    /// </summary>
    [Fact]
    public async Task CreateUserAsync_ShouldNotReportAUsernameConflict_ForACollisionOnAnotherColumn()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(email: "duplicate@example.com", username: "first-name");

        var act = () => harness.Repository.CreateUserAsync(new User
        {
            Email = "duplicate@example.com",
            Username = "second-name",
            Password = "hashed-password",
            Usertype = "participant"
        });

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CreateUserAsync_ShouldRemoveExpiredReservation_WhenUsernameIsClaimed()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync();
        harness.Db.UsernameReservations.Add(new UsernameReservation
        {
            Username = "available-name",
            UserId = ownerId,
            ReservedUntilUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        await harness.Db.SaveChangesAsync();

        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "claimant@example.com",
            Username = "  AVAILABLE-NAME  ",
            Password = "hashed-password",
            Usertype = "participant",
        });

        created.Username.Should().Be("available-name");
        (await harness.Db.UsernameReservations.FindAsync("available-name")).Should().BeNull();
    }

    [Fact]
    public async Task CreateUserAsync_ShouldRejectActiveReservation()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var ownerId = await harness.SeedUserAsync();
        harness.Db.UsernameReservations.Add(new UsernameReservation
        {
            Username = "reserved-name",
            UserId = ownerId,
            ReservedUntilUtc = DateTime.UtcNow.AddMinutes(1),
        });
        await harness.Db.SaveChangesAsync();

        var act = () => harness.Repository.CreateUserAsync(new User
        {
            Email = "claimant@example.com",
            Username = "reserved-name",
            Password = "hashed-password",
            Usertype = "participant",
        });

        await act.Should().ThrowAsync<UsernameTakenException>();
    }

    [Fact]
    public async Task ExplicitTransactions_ShouldRunInsideConfiguredRetryingExecutionStrategy()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            retryingExecutionStrategy: true);
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        var user = await harness.Repository.CreateUserAsync(new User
        {
            Email = "retrying-strategy@example.com",
            Username = "strategy-user",
            Password = "hashed-password",
            Usertype = "participant",
        });
        var changed = await harness.Repository.ChangeUsernameAsync(
            user.Id,
            "strategy-renamed",
            "strategy-renamed",
            now,
            now.AddDays(30));

        changed.Status.Should().Be(UsernameChangeStatus.Changed);
        changed.User!.Username.Should().Be("strategy-renamed");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldReportTheRealPredecessor_WhenACommitAcknowledgementIsLost()
    {
        // The commit reaches the database and then reports a transient failure, so the execution
        // strategy re-runs the delegate. That re-run finds this call own URL already stored; it
        // must still report what the first attempt replaced, or the caller never deletes the real
        // predecessor and it stays in storage referenced by nothing.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            loseFirstCommitAcknowledgement: true);
        var userId = await harness.SeedUserAsync();
        await harness.Db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Avatar, "https://cdn.test/users/original.webp"));

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/next.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().Be("https://cdn.test/users/original.webp");
        swap.User.Avatar.Should().Be("https://cdn.test/users/next.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldRefuseToWriteAgain_WhenAnAmbiguousCommitWasSuperseded()
    {
        // The worst interleaving: this call commits, loses the acknowledgement, and another upload
        // replaces its URL and deletes that blob before the re-run. Writing the URL again would
        // point the account at bytes that no longer exist, so the swap gives up. The caller then
        // deletes the blob it uploaded, and the account keeps the other request's avatar.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            loseFirstCommitAcknowledgement: true);
        var userId = await harness.SeedUserAsync();
        await harness.Db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Avatar, "https://cdn.test/users/original.webp"));

        await using var other = harness.CreateSeparateScopeRepository();
        harness.CommitInterceptor!.WhileAcknowledgementIsLost = () =>
            other.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/other.webp")
                .GetAwaiter().GetResult();

        var act = () => harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/mine.webp");

        // The conflict carries what this attempt replaced. If its write did commit, this is the
        // only report of that URL anyone gets, and OrphanBlobCleanup is opt-in.
        (await act.Should().ThrowAsync<AvatarSwapSupersededException>())
            .Which.ReplacedAvatarUrl.Should().Be("https://cdn.test/users/original.webp");

        // The other request's avatar stands, and its blob is one that exists.
        var stored = await harness.Db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Avatar).SingleAsync();
        stored.Should().Be("https://cdn.test/users/other.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldRetryNormally_WhenTheReloadFaultsBeforeAnyCommit()
    {
        // The update matched, but the reload that reports the result faulted, so the transaction
        // rolled back without a commit ever being attempted. Nothing was written, so the re-run
        // must swap normally: treating it as ambiguous would answer 409 to a user who did nothing
        // wrong, and have the caller delete a blob the account still points at.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            failFirstAvatarReload: true);
        var userId = await harness.SeedUserAsync();
        await harness.Db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Avatar, "https://cdn.test/users/live.webp"));
        harness.ReloadFailer!.Arm();

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/mine.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().Be("https://cdn.test/users/live.webp");
        swap.User.Avatar.Should().Be("https://cdn.test/users/mine.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldRetryNormally_WhenAWriteFreeAttemptFaultsOnCommit()
    {
        // The attempt's update matches no rows because another request got there first, and the
        // commit of that empty transaction then faults. Nothing was written, so the re-run must
        // swap normally rather than report a conflict the user did not cause.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            loseFirstCommitAcknowledgement: true,
            suppressFirstAvatarUpdate: true);
        var userId = await harness.SeedUserAsync();
        await harness.Db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Avatar, "https://cdn.test/users/live.webp"));
        harness.UpdateSuppressor!.Arm();

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/mine.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().Be("https://cdn.test/users/live.webp");
        swap.User.Avatar.Should().Be("https://cdn.test/users/mine.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldSwapNormally_AfterATransientFailureRollsTheFirstAttemptBack()
    {
        // The update fails and the transaction rolls back, so nothing was written. A re-run must
        // treat this as an ordinary first attempt: the avatar it reads is still the live one, and
        // reporting it as replaced without writing would have the caller delete a blob the account
        // is still using.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            failFirstAvatarUpdate: true);
        var userId = await harness.SeedUserAsync();
        await harness.Db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.Avatar, "https://cdn.test/users/live.webp"));
        harness.UpdateFailer!.Arm();

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/new.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().Be("https://cdn.test/users/live.webp");
        swap.User.Avatar.Should().Be("https://cdn.test/users/new.webp");

        var stored = await harness.Db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Avatar).SingleAsync();
        stored.Should().Be("https://cdn.test/users/new.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldRunInsideConfiguredRetryingExecutionStrategy()
    {
        // The swap commits its update and the reload that reports the result together, so it opens
        // a transaction. Starting one outside the configured execution strategy throws, which is
        // what this pins.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync(
            retryingExecutionStrategy: true);
        var userId = await harness.SeedUserAsync();

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/first.webp");

        swap.Should().NotBeNull();
        swap!.User.Avatar.Should().Be("https://cdn.test/users/first.webp");
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateKnownFields_AndNormalizeRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdateUserAsync(userId, new User
        {
            Email = "ignored@example.com",
            Password = "new-hash",
            Usertype = "admin",
            Name = "Updated Name",
            Username = "updated-user",
            Avatar = "/avatars/updated.png",
            Address = "123 Updated Street",
            Phone = "555-0100"
        });

        updated.Should().NotBeNull();
        updated!.Password.Should().Be("new-hash");
        updated.Usertype.Should().Be("Admin");
        updated.Name.Should().Be("Updated Name");
        updated.Username.Should().Be("seed-user");
        updated.Avatar.Should().Be("/avatars/updated.png");
        updated.Address.Should().Be("123 Updated Street");
        updated.Phone.Should().Be("555-0100");
        updated.Email.Should().Be("seed@example.com");
    }

    [Fact]
    public async Task UpdatePartialAsync_ShouldChangeMutableFields_ButPreserveIdentityAndRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdatePartialAsync(new User
        {
            Id = userId,
            Email = "partial@example.com",
            Usertype = "organizer",
            Name = "Partial Name"
        });

        updated.Should().NotBeNull();
        // Identity and role are intentionally NOT mutable via UpdatePartialAsync: even when
        // Email/Usertype are supplied they must be ignored, so a stale JWT claim can never
        // silently overwrite them. Email changes require re-verification and role changes go
        // through dedicated admin/status flows.
        updated!.Email.Should().Be("seed@example.com");
        updated.Usertype.Should().Be("participant");
        // Mutable profile fields are still applied.
        updated.Name.Should().Be("Partial Name");
        updated.Username.Should().Be("seed-user");
        updated.Password.Should().Be("seed-password");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldReplaceTheAvatarAndReportThePreviousOne()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();
        await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/first.webp");

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/second.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().Be("https://cdn.test/users/first.webp");
        swap.User.Avatar.Should().Be("https://cdn.test/users/second.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldReportNoPreviousAvatar_ForAnAccountThatNeverHadOne()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        // The seeded user carries an avatar; clear it so the null branch of the conditional
        // update is the one under test.
        var seeded = await harness.Db.Users.FindAsync(userId);
        seeded!.Avatar = null;
        await harness.Db.SaveChangesAsync();

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/first.webp");

        swap.Should().NotBeNull();
        swap!.PreviousAvatar.Should().BeNull();
        swap.User.Avatar.Should().Be("https://cdn.test/users/first.webp");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldWriteOnlyTheAvatarColumn()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        // Stands in for a profile edit saved while the image was being processed: it must survive.
        await harness.Repository.UpdatePartialAsync(new User
        {
            Id = userId,
            Email = "seed@example.com",
            Usertype = "participant",
            Name = "Renamed Meanwhile",
            Phone = "555-9999"
        });

        var swap = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/first.webp");

        swap.Should().NotBeNull();
        swap!.User.Name.Should().Be("Renamed Meanwhile");
        swap.User.Phone.Should().Be("555-9999");
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldReturnNull_WhenTheAccountIsGone()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var swap = await harness.Repository.SwapAvatarAsync(4242, "https://cdn.test/users/first.webp");

        swap.Should().BeNull();
    }

    [Fact]
    public async Task SwapAvatarAsync_ShouldNotOrphanABlob_WhenAnotherRequestSwapsFirst()
    {
        // Two uploads for one account run in separate scopes with their own DbContext, so both can
        // read the same predecessor. The conditional update makes the loser retry and report the
        // winner's URL, so between them every superseded blob is reported exactly once and none is
        // left unreferenced.
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();
        await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/original.webp");

        await using var other = harness.CreateSeparateScopeRepository();

        var first = await harness.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/first.webp");
        var second = await other.Repository.SwapAvatarAsync(userId, "https://cdn.test/users/second.webp");

        first!.PreviousAvatar.Should().Be("https://cdn.test/users/original.webp");
        second!.PreviousAvatar.Should().Be("https://cdn.test/users/first.webp");
        second.User.Avatar.Should().Be("https://cdn.test/users/second.webp");

        // Every URL that is no longer current was reported to exactly one caller for deletion.
        new[] { first.PreviousAvatar, second.PreviousAvatar }
            .Should().OnlyHaveUniqueItems()
            .And.NotContain(second.User.Avatar);
    }

    [Fact]
    public async Task UpdateProviderIdsAsync_ShouldUpdateProviderValues_AndReturnOAuthRecord()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdateProviderIdsAsync(userId, "google-123", "ms-456");

        updated.Should().NotBeNull();
        updated!.GoogleID.Should().Be("google-123");
        updated.MicrosoftID.Should().Be("ms-456");
        updated.Usertype.Should().Be("Participant");
    }

    [Fact]
    public async Task UpdateUserStatusAsync_ShouldToggleDisabledState_ClearReasonOnEnable_AndIncrementAuthVersion()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var disabled = await harness.Repository.UpdateUserStatusAsync(userId, true, "policy");
        disabled.Should().NotBeNull();
        disabled!.IsDisabled.Should().BeTrue();
        disabled.DisabledReason.Should().Be("policy");
        disabled.DisabledAtUtc.Should().NotBeNull();
        disabled.AuthVersion.Should().Be(2);

        var enabled = await harness.Repository.UpdateUserStatusAsync(userId, false, "ignored");
        enabled.Should().NotBeNull();
        enabled!.IsDisabled.Should().BeFalse();
        enabled.DisabledReason.Should().BeNull();
        enabled.DisabledAtUtc.Should().BeNull();
        enabled.AuthVersion.Should().Be(3);
    }

    [Fact]
    public async Task IncrementAuthVersionAsync_AndDeleteUserAsync_ShouldHandlePresentAndMissingUsers()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        (await harness.Repository.IncrementAuthVersionAsync(userId)).Should().BeTrue();
        (await harness.Db.Users.SingleAsync(user => user.Id == userId)).AuthVersion.Should().Be(2);
        (await harness.Repository.IncrementAuthVersionAsync(9999)).Should().BeFalse();

        // Deleting a present user returns the blob URLs (here just the avatar) it orphaned;
        // deleting a missing user returns an empty list.
        (await harness.Repository.DeleteUserAsync(userId)).Should().Contain("/avatars/seed.png");
        (await harness.Repository.DeleteUserAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserAsync_AndCredentialLookups_ShouldProjectSanitizedAndAuthViews()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var user = await harness.Repository.GetUserAsync(userId);
        var authByUsername = await harness.Repository.GetAuthByUsernameAsync("seed-user");
        var auth = await harness.Repository.GetAuthByEmailAsync("seed@example.com");

        user.Should().NotBeNull();
        user!.Password.Should().BeNull();
        user.Usertype.Should().Be("Participant");
        user.Username.Should().Be("seed-user");

        auth.Should().NotBeNull();
        authByUsername.Should().BeEquivalentTo(auth);
        auth!.Password.Should().Be("seed-password");
        auth.Usertype.Should().Be("Participant");
        auth.IsDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task OAuthLookupMethods_ShouldReturnNormalizedRecords()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(googleId: "google-seed", microsoftId: "ms-seed");

        var byEmail = await harness.Repository.GetOAuthByEmailAsync("seed@example.com");
        var byGoogle = await harness.Repository.GetOAuthByGoogleIdAsync("google-seed");
        var byMicrosoft = await harness.Repository.GetOAuthByMicrosoftIdAsync("ms-seed");

        byEmail.Should().NotBeNull();
        byEmail!.GoogleID.Should().Be("google-seed");
        byEmail.MicrosoftID.Should().Be("ms-seed");
        byEmail.Usertype.Should().Be("Participant");

        byGoogle!.Email.Should().Be("seed@example.com");
        byMicrosoft!.Email.Should().Be("seed@example.com");
    }

    [Fact]
    public async Task GetProfileByUsernameAsync_ShouldFallbackToEmail_WhenUsernameIsBlank()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(username: "");

        var profile = await harness.Repository.GetProfileByUsernameAsync("");

        profile.Should().NotBeNull();
        profile!.Username.Should().Be("seed@example.com");
        profile.Usertype.Should().Be("Participant");
    }

    /// <summary>
    /// The AuthVersion bump has to land in the same commit as the address. JwtConfiguration checks
    /// the claim on every request, so a change that committed without it would leave live tokens
    /// authenticating as an address the account no longer holds.
    /// </summary>
    [Fact]
    public async Task ChangeEmailAsync_ShouldSwapTheAddress_AndInvalidateOutstandingTokens()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(email: "old@example.com");
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        var result = await harness.Repository.ChangeEmailAsync(userId, "new@example.com", 1, now);

        result.Status.Should().Be(EmailChangeStatus.Changed);
        result.PreviousEmail.Should().Be("old@example.com");
        result.User!.Email.Should().Be("new@example.com");
        result.User.AuthVersion.Should().Be(2);
        result.User.UpdatedAt.Should().Be(now);

        var stored = await harness.Db.Users.SingleAsync(user => user.Id == userId);
        stored.Email.Should().Be("new@example.com");
        stored.AuthVersion.Should().Be(2);
    }

    [Fact]
    public async Task ChangeEmailAsync_ShouldReportUnavailable_WhenAnotherAccountHoldsTheAddress()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(email: "mine@example.com", username: "mine");
        await harness.SeedUserAsync(email: "taken@example.com", username: "theirs");
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        var result = await harness.Repository.ChangeEmailAsync(userId, "taken@example.com", 1, now);

        result.Status.Should().Be(EmailChangeStatus.Unavailable);

        var stored = await harness.Db.Users.SingleAsync(user => user.Id == userId);
        stored.Email.Should().Be("mine@example.com");
        stored.AuthVersion.Should().Be(1);
    }

    /// <summary>
    /// The address column is citext in production, so a change that only alters casing is not a
    /// change at all and must not burn a session for nothing.
    /// </summary>
    [Fact]
    public async Task ChangeEmailAsync_ShouldReportUnchanged_ForTheSameAddressInAnotherCasing()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(email: "same@example.com");
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        var result = await harness.Repository.ChangeEmailAsync(userId, "SAME@Example.com", 1, now);

        result.Status.Should().Be(EmailChangeStatus.Unchanged);

        var stored = await harness.Db.Users.SingleAsync(user => user.Id == userId);
        stored.Email.Should().Be("same@example.com");
        stored.AuthVersion.Should().Be(1);
    }

    [Fact]
    public async Task ChangeEmailAsync_ShouldReportUserNotFound_ForAnUnknownAccount()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        var result = await harness.Repository.ChangeEmailAsync(9999, "ghost@example.com", 1, now);

        result.Status.Should().Be(EmailChangeStatus.UserNotFound);
    }

    /// <summary>
    /// The version is enforced while the row is locked, so a credential rotation that commits
    /// between the caller's read and this write still stops the change.
    /// </summary>
    [Fact]
    public async Task ChangeEmailAsync_ShouldReportStale_WhenTheAuthVersionMovedOn()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(email: "stale@example.com");
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        // The account is at version 1; the proof was issued against a version it has left behind.
        var result = await harness.Repository.ChangeEmailAsync(userId, "new@example.com", 99, now);

        result.Status.Should().Be(EmailChangeStatus.Stale);

        var stored = await harness.Db.Users.SingleAsync(user => user.Id == userId);
        stored.Email.Should().Be("stale@example.com");
        stored.AuthVersion.Should().Be(1);
    }

    [Fact]
    public async Task GetAuthByIdAsync_ShouldReturnCredentialsAndDisplayName()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(email: "byid@example.com", role: "organizer");

        var record = await harness.Repository.GetAuthByIdAsync(userId);

        record.Should().NotBeNull();
        record!.Email.Should().Be("byid@example.com");
        record.Password.Should().Be("seed-password");
        record.Usertype.Should().Be("Organizer");
        record.Name.Should().Be("Seed User");
    }

    [Fact]
    public async Task ChangeUsernameAsync_ShouldReserveOldUsername_AndResolvePublicAlias()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(username: "old-name");
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var availableAt = now.AddDays(30);

        var result = await harness.Repository.ChangeUsernameAsync(
            userId,
            "new-name",
            "new-name",
            now,
            availableAt);

        result.Status.Should().Be(UsernameChangeStatus.Changed);
        result.User!.Username.Should().Be("new-name");
        result.User.UsernameChangeAvailableAtUtc.Should().Be(availableAt);
        (await harness.Repository.UsernameUnavailableAsync("old-name", now)).Should().BeTrue();

        var alias = await harness.Repository.GetPublicProfileByUsernameOrReservationAsync(
            "old-name",
            now);
        alias!.Username.Should().Be("new-name");

        (await harness.Repository.GetPublicProfileByUsernameOrReservationAsync(
            "old-name",
            availableAt)).Should().BeNull();
        (await harness.Repository.UsernameUnavailableAsync("old-name", availableAt)).Should().BeFalse();
    }

    [Fact]
    public async Task ChangeUsernameAsync_ShouldEnforceCooldown_AtAnExclusiveBoundary()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(username: "first-name");
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        var availableAt = now.AddDays(30);

        (await harness.Repository.ChangeUsernameAsync(
            userId,
            "second-name",
            "second-name",
            now,
            availableAt)).Status.Should().Be(UsernameChangeStatus.Changed);

        var blocked = await harness.Repository.ChangeUsernameAsync(
            userId,
            "third-name",
            "third-name",
            availableAt.AddTicks(-1),
            availableAt.AddDays(30));
        blocked.Status.Should().Be(UsernameChangeStatus.CooldownActive);
        blocked.AvailableAtUtc.Should().Be(availableAt);

        var allowed = await harness.Repository.ChangeUsernameAsync(
            userId,
            "third-name",
            "third-name",
            availableAt,
            availableAt.AddDays(30));
        allowed.Status.Should().Be(UsernameChangeStatus.Changed);
    }

    [Fact]
    public async Task ChangeUsernameAsync_FirstAssignment_ShouldNotStartCooldown()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(username: "");
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        var first = await harness.Repository.ChangeUsernameAsync(
            userId,
            "first-name",
            "first-name",
            now,
            now.AddDays(30));

        first.Status.Should().Be(UsernameChangeStatus.Changed);
        first.User!.UsernameChangeAvailableAtUtc.Should().BeNull();

        var second = await harness.Repository.ChangeUsernameAsync(
            userId,
            "second-name",
            "second-name",
            now.AddMinutes(1),
            now.AddDays(30).AddMinutes(1));
        second.Status.Should().Be(UsernameChangeStatus.Changed);
    }

    [Fact]
    public async Task GetUsersAsync_GetByIdsAsync_AndEmailExistsAsync_ShouldRespectFiltersOrderingAndDetailLevel()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var firstId = await harness.SeedUserAsync(
            email: "first@example.com",
            role: "Participant",
            username: "first-user",
            disabled: true);
        var secondId = await harness.SeedUserAsync(
            email: "second@example.com",
            role: "Organizer",
            username: "second-user");

        var organizers = await harness.Repository.GetUsersAsync("Organizer", UserReadDetailLevel.Slim);
        var admins = await harness.Repository.GetByIdsAsync([secondId, firstId], UserReadDetailLevel.Admin);

        organizers.Should().ContainSingle();
        organizers[0].Email.Should().Be("second@example.com");
        organizers[0].IsDisabled.Should().BeNull();

        admins.Select(user => user.Id).Should().Equal(secondId, firstId);
        admins[1].IsDisabled.Should().BeTrue();
        admins[1].DisabledReason.Should().Be("disabled");
        admins[1].CreatedAt.Should().NotBeNull();

        (await harness.Repository.GetByIdsAsync([], UserReadDetailLevel.Slim)).Should().BeEmpty();
        (await harness.Repository.EmailExistsAsync("first@example.com")).Should().BeTrue();
        (await harness.Repository.EmailExistsAsync("missing@example.com")).Should().BeFalse();
    }

    /// <summary>
    /// The batched lookup composes two tables into one query. A union that lost either half would
    /// still look correct for most inputs, so both halves and the expiry boundary are pinned here:
    /// dropping the reservations half would hand out names that are still cooling down after a
    /// rename, and dropping the users half would hand out names that are simply taken.
    /// </summary>
    [Fact]
    public async Task FindUnavailableUsernamesAsync_ShouldReportHeldAndReservedNamesTogether()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var utcNow = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var ownerId = await harness.SeedUserAsync(
            email: "holder@example.com", username: "held-name");
        harness.Db.UsernameReservations.Add(new UsernameReservation
        {
            Username = "cooling-name",
            UserId = ownerId,
            ReservedUntilUtc = utcNow.AddDays(1),
        });
        harness.Db.UsernameReservations.Add(new UsernameReservation
        {
            Username = "released-name",
            UserId = ownerId,
            ReservedUntilUtc = utcNow.AddMinutes(-1),
        });
        await harness.Db.SaveChangesAsync();

        var taken = await harness.Repository.FindUnavailableUsernamesAsync(
            ["held-name", "cooling-name", "released-name", "never-used"],
            utcNow);

        taken.Should().BeEquivalentTo(["held-name", "cooling-name"]);
    }

    [Fact]
    public async Task FindUnavailableUsernamesAsync_ShouldReportNothing_WhenGivenNothing()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var taken = await harness.Repository.FindUnavailableUsernamesAsync(
            [], DateTime.UtcNow);

        taken.Should().BeEmpty();
    }

    /// <summary>
    /// GetUserAsync hand-projects a sanitised User rather than returning the tracked entity, so any
    /// column added to the table has to be added here too. Missing this is invisible on the write
    /// path — the row is stored correctly — and only shows up as the display form reverting to the
    /// lowercase key on every read, which is exactly what shipped before this test existed.
    /// </summary>
    [Fact]
    public async Task GetUserAsync_ShouldProjectTheDisplayForm()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "projected@example.com",
            Username = "ThomasT",
            Password = "hashed-password",
            Usertype = "participant"
        });

        var fetched = await harness.Repository.GetUserAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Username.Should().Be("thomast");
        fetched.UsernameDisplay.Should().Be("ThomasT");
    }

    /// <summary>
    /// The choke point: every account creation passes through here, so a caller that supplies no
    /// display form — a signup stashed in a verification token before the column existed — still
    /// lands a row satisfying Normalize(display) == Username.
    /// </summary>
    [Fact]
    public async Task CreateUserAsync_ShouldFillADisplayTheCallerOmitted()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "no-display@example.com",
            Username = "PlainName",
            UsernameDisplay = null,
            Password = "hashed-password",
            Usertype = "participant"
        });

        created.Username.Should().Be("plainname");
        created.UsernameDisplay.Should().Be("PlainName");
    }

    /// <summary>
    /// A display that does not lowercase to the username is corruption, not a rename, so it is
    /// replaced rather than trusted.
    /// </summary>
    [Fact]
    public async Task CreateUserAsync_ShouldReplaceADisplayThatDoesNotMatchTheUsername()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "mismatch@example.com",
            Username = "realname",
            UsernameDisplay = "SomethingElse",
            Password = "hashed-password",
            Usertype = "participant"
        });

        created.UsernameDisplay.Should().Be("realname");
    }

    /// <summary>
    /// UserListRecord.UsernameDisplay is non-nullable, and the slim projection is the default one:
    /// it backs GetByIdsAsync, which feeds club post, discussion, comment, review and follow author
    /// lookups. A projection that skipped the column would leave a declared-non-null field null on
    /// every non-admin read, so both detail levels are checked rather than just the admin one.
    /// </summary>
    [Theory]
    [InlineData(UserReadDetailLevel.Slim)]
    [InlineData(UserReadDetailLevel.Admin)]
    public async Task GetByIdsAsync_ShouldProjectTheDisplayForm(UserReadDetailLevel detail)
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "listed@example.com",
            Username = "ThomasT",
            Password = "hashed-password",
            Usertype = "participant"
        });

        var listed = await harness.Repository.GetByIdsAsync([created.Id], detail);

        listed.Should().ContainSingle();
        listed[0].Username.Should().Be("thomast");
        listed[0].UsernameDisplay.Should().Be("ThomasT");
    }

    /// <summary>
    /// A row that predates the display column reads back with the lookup key rather than null, so
    /// the non-nullable contract holds for the backfill population too.
    /// </summary>
    [Fact]
    public async Task GetByIdsAsync_ShouldFallBackToTheUsername_WhenNoDisplayWasStored()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync(
            email: "legacy-list@example.com", username: "legacy-name");
        var stored = await harness.Db.Users.SingleAsync(user => user.Id == userId);
        stored.UsernameDisplay = null;
        await harness.Db.SaveChangesAsync();

        var listed = await harness.Repository.GetByIdsAsync([userId]);

        listed[0].UsernameDisplay.Should().Be("legacy-name");
    }

    private sealed class AuthUserRepositoryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public AuthUserRepository Repository { get; }

        private AuthUserRepositoryHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            CommitAcknowledgementLosingInterceptor? commitInterceptor = null)
        {
            _connection = connection;
            Db = db;
            Repository = new AuthUserRepository(db);
            CommitInterceptor = commitInterceptor;
        }

        public CommitAcknowledgementLosingInterceptor? CommitInterceptor { get; }

        public AvatarUpdateFailingInterceptor? UpdateFailer { get; private set; }

        public AvatarUpdateSuppressingInterceptor? UpdateSuppressor { get; private set; }

        public AvatarReloadFailingInterceptor? ReloadFailer { get; private set; }

        public static async Task<AuthUserRepositoryHarness> CreateAsync(
            bool retryingExecutionStrategy = false,
            bool loseFirstCommitAcknowledgement = false,
            bool failFirstAvatarUpdate = false,
            bool suppressFirstAvatarUpdate = false,
            bool failFirstAvatarReload = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var optionsBuilder = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection);
            if (retryingExecutionStrategy
                || loseFirstCommitAcknowledgement
                || failFirstAvatarUpdate
                || suppressFirstAvatarUpdate
                || failFirstAvatarReload)
            {
                optionsBuilder.ReplaceService<
                    IExecutionStrategyFactory,
                    RetryingExecutionStrategyFactory>();
            }

            CommitAcknowledgementLosingInterceptor? commitInterceptor = null;
            if (loseFirstCommitAcknowledgement)
            {
                commitInterceptor = new CommitAcknowledgementLosingInterceptor();
                optionsBuilder.AddInterceptors(commitInterceptor);
            }

            AvatarUpdateFailingInterceptor? updateInterceptor = null;
            if (failFirstAvatarUpdate)
            {
                updateInterceptor = new AvatarUpdateFailingInterceptor();
                optionsBuilder.AddInterceptors(updateInterceptor);
            }

            AvatarUpdateSuppressingInterceptor? suppressInterceptor = null;
            if (suppressFirstAvatarUpdate)
            {
                suppressInterceptor = new AvatarUpdateSuppressingInterceptor();
                optionsBuilder.AddInterceptors(suppressInterceptor);
            }

            AvatarReloadFailingInterceptor? reloadInterceptor = null;
            if (failFirstAvatarReload)
            {
                reloadInterceptor = new AvatarReloadFailingInterceptor();
                optionsBuilder.AddInterceptors(reloadInterceptor);
            }

            var db = new AppDatabaseContext(optionsBuilder.Options);
            await db.Database.EnsureCreatedAsync();

            // Armed only now: schema creation commits too, and losing that commit would re-run
            // EnsureCreated against tables that already exist.
            // Only the commit interceptor is armed here. The command interceptors would
            // otherwise swallow or fail a test's own setup writes, so tests arm them once the row
            // is in the state they want.
            commitInterceptor?.Arm();

            return new AuthUserRepositoryHarness(connection, db, commitInterceptor)
            {
                UpdateFailer = updateInterceptor,
                UpdateSuppressor = suppressInterceptor,
                ReloadFailer = reloadInterceptor
            };
        }

        public async Task<int> SeedUserAsync(
            string email = "seed@example.com",
            string role = "participant",
            string username = "seed-user",
            string? googleId = null,
            string? microsoftId = null,
            bool disabled = false)
        {
            var user = new User
            {
                Email = email,
                Password = "seed-password",
                Usertype = role,
                Name = "Seed User",
                Username = username,
                Avatar = "/avatars/seed.png",
                Address = "1 Seed Street",
                Phone = "555-0000",
                GoogleID = googleId,
                MicrosoftID = microsoftId,
                IsDisabled = disabled,
                DisabledAtUtc = disabled ? DateTime.UtcNow : null,
                DisabledReason = disabled ? "disabled" : null,
                AuthVersion = 1
            };

            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user.Id;
        }

        /// <summary>
        /// A second repository over its own <see cref="AppDatabaseContext"/>, sharing this
        /// harness's database. It stands in for a concurrent request, which gets its own scoped
        /// context and therefore its own change tracker and reads.
        /// </summary>
        public SeparateScope CreateSeparateScopeRepository()
        {
            var db = new AppDatabaseContext(
                new DbContextOptionsBuilder<AppDatabaseContext>().UseSqlite(_connection).Options);
            return new SeparateScope(db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class SeparateScope(AppDatabaseContext db) : IAsyncDisposable
    {
        public AuthUserRepository Repository { get; } = new(db);

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class RetryingExecutionStrategyFactory(
        ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryingExecutionStrategy(dependencies);
    }

    private sealed class RetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 3, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is LostCommitAcknowledgementException;
    }

    /// <summary>
    /// Stands in for the commit that reaches the database and then fails to report back, which is
    /// what <c>EnableRetryOnFailure</c> turns into a re-run of the whole delegate.
    /// </summary>
    private sealed class LostCommitAcknowledgementException() : Exception("Simulated lost commit acknowledgement.");

    /// <summary>
    /// Fails the first UPDATE of the avatar column, so the surrounding transaction rolls back with
    /// nothing written: the ordinary transient-failure shape, as opposed to a lost commit.
    /// </summary>
    private sealed class AvatarUpdateFailingInterceptor : DbCommandInterceptor
    {
        private bool _armed;
        private bool _alreadyFailed;

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && !_alreadyFailed && command.CommandText.Contains("Avatar", StringComparison.Ordinal))
            {
                _alreadyFailed = true;
                throw new LostCommitAcknowledgementException();
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Makes the first UPDATE of the avatar column report that it matched no rows, which is what a
    /// request that lost the race sees, without needing a second connection to interleave on.
    /// </summary>
    private sealed class AvatarUpdateSuppressingInterceptor : DbCommandInterceptor
    {
        private bool _armed;
        private bool _alreadySuppressed;

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && !_alreadySuppressed && command.CommandText.Contains("Avatar", StringComparison.Ordinal))
            {
                _alreadySuppressed = true;
                return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Fails the first SELECT that runs after an avatar UPDATE, which is the in-transaction reload:
    /// the transaction rolls back with no commit attempted.
    /// </summary>
    private sealed class AvatarReloadFailingInterceptor : DbCommandInterceptor
    {
        private bool _armed;
        private bool _sawUpdate;
        private bool _alreadyFailed;

        public void Arm() => _armed = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && command.CommandText.Contains("Avatar", StringComparison.Ordinal))
                _sawUpdate = true;

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && _sawUpdate && !_alreadyFailed)
            {
                _alreadyFailed = true;
                throw new LostCommitAcknowledgementException();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CommitAcknowledgementLosingInterceptor : DbTransactionInterceptor
    {
        private bool _armed;
        private bool _alreadyLost;

        /// <summary>Runs after the commit landed and before the failure is reported.</summary>
        public Action? WhileAcknowledgementIsLost { get; set; }

        public void Arm() => _armed = true;

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            ThrowOnce();
            base.TransactionCommitted(transaction, eventData);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ThrowOnce();
            return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }

        private void ThrowOnce()
        {
            if (!_armed || _alreadyLost)
                return;

            _alreadyLost = true;
            WhileAcknowledgementIsLost?.Invoke();
            throw new LostCommitAcknowledgementException();
        }
    }
}
