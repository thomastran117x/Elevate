using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa;
using backend.main.features.auth.oauth.links;
using backend.main.features.auth.passkeys;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class PasskeyManagementServiceTests
{
    private const int UserId = 7;
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PasskeyAccount Account = new(UserId, "owner@example.com", "sid-1", 3);
    private static readonly byte[] Handle = [1, 2, 3, 4];
    private static readonly JsonElement Response = JsonDocument.Parse("{}").RootElement;

    private readonly Mock<IPasskeyCredentialRepository> _passkeys = new();
    private readonly Mock<IAuthUserRepository> _users = new();
    private readonly Mock<IUserIdentityLinkRepository> _links = new();
    private readonly Mock<IPasskeyCeremonyStore> _ceremonies = new();
    private readonly Mock<IWebAuthnRelyingParty> _relyingParty = new();
    private readonly Mock<IMfaFactorChangeService> _factorChanges = new();
    private readonly RecordingAuthSecurityEventEmitter _events = new();
    private readonly List<PasskeyCredential> _stored = [];

    public PasskeyManagementServiceTests()
    {
        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record());
        _links.Setup(l => l.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _passkeys.Setup(p => p.ListForUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored.ToList());
        _passkeys.Setup(p => p.GetOrCreateUserHandleAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Handle);
        _passkeys.Setup(p => p.AddAsync(It.IsAny<PasskeyCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PasskeyCredential, int, CancellationToken>((passkey, _, _) =>
            {
                passkey.Id = 42;
                _stored.Add(passkey);
            })
            .ReturnsAsync(PasskeyAddResult.Added);
        _ceremonies.Setup(c => c.CreateAsync(It.IsAny<PasskeyCeremony>()))
            .Callback<PasskeyCeremony>(ceremony => ceremony.ExpiresAtUtc = Now.AddMinutes(5))
            .ReturnsAsync("ceremony-id");
        _relyingParty.Setup(r => r.CreateRegistrationOptions(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<byte[]>>()))
            .Returns("{\"challenge\":\"abc\"}");
    }

    [Fact]
    public async Task ListAsync_ShouldShowEachPasskeyAndWhetherItCanBeRemoved()
    {
        _stored.Add(Stored(1, backupEligible: true));
        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record(password: null));

        var listed = await Service().ListAsync(UserId);

        listed.Limit.Should().Be(3);
        var passkey = listed.Passkeys.Should().ContainSingle().Subject;
        passkey.Name.Should().Be("Passkey 1");
        passkey.Syncable.Should().BeTrue();
        passkey.CanRemove.Should().BeFalse("it is the only way to sign in to a passwordless account");
    }

    [Fact]
    public async Task StartRegistrationAsync_ShouldBindTheCeremonyToTheAccountSessionAndAuthVersion()
    {
        _stored.Add(Stored(1));
        PasskeyCeremony? created = null;
        _ceremonies.Setup(c => c.CreateAsync(It.IsAny<PasskeyCeremony>()))
            .Callback<PasskeyCeremony>(ceremony => created = ceremony)
            .ReturnsAsync("ceremony-id");

        var options = await Service().StartRegistrationAsync(Account, "  Work laptop  ");

        options.CeremonyId.Should().Be("ceremony-id");
        options.PublicKey.GetProperty("challenge").GetString().Should().Be("abc");
        created!.Type.Should().Be(PasskeyCeremonyType.Registration);
        created.UserId.Should().Be(UserId);
        created.SessionId.Should().Be("sid-1");
        created.AuthVersion.Should().Be(3);
        created.FriendlyName.Should().Be("Work laptop");
        _relyingParty.Verify(r => r.CreateRegistrationOptions(
            Handle,
            "owner",
            "Owner Name",
            It.Is<IReadOnlyList<byte[]>>(ids => ids.Count == 1)));
    }

    [Fact]
    public async Task StartRegistrationAsync_ShouldRefuse_AtTheLimit()
    {
        _stored.AddRange([Stored(1), Stored(2), Stored(3)]);

        var act = () => Service().StartRegistrationAsync(Account, null);

        (await act.Should().ThrowAsync<PasskeyConflictException>()).Which.ErrorCode
            .Should().Be(PasskeyConflictException.LimitReachedCode);
    }

    [Fact]
    public async Task StartRegistrationAsync_ShouldRefuseADisabledOrMissingAccount()
    {
        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record(disabled: true));
        await Service().Invoking(s => s.StartRegistrationAsync(Account, null)).Should().ThrowAsync<ForbiddenException>();

        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync((UserAuthRecord?)null);
        await Service().Invoking(s => s.StartRegistrationAsync(Account, null)).Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ShouldStoreThePasskey_AndRecordTheFactorChange()
    {
        GivenCeremony(Ceremony());
        GivenVerified(new VerifiedPasskeyRegistration([9, 9], [5, 5], 0, Guid.Empty, "internal,hybrid", "none", true, false));

        var passkey = await Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        passkey.Id.Should().Be(42);
        passkey.Name.Should().Be("Laptop");
        passkey.Syncable.Should().BeTrue();
        var stored = _stored.Single();
        stored.UserHandle.Should().Equal(Handle);
        stored.CredentialId.Should().Equal(9, 9);
        stored.Transports.Should().Be("internal,hybrid");
        stored.CreatedAtUtc.Should().Be(Now);
        _factorChanges.Verify(f => f.RecordAsync(UserId, "owner@example.com", MfaFactorKind.Passkey, MfaFactorChange.Added));
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ShouldNameAnUnnamedPasskeyByDefault()
    {
        var ceremony = Ceremony();
        ceremony.FriendlyName = null;
        GivenCeremony(ceremony);
        GivenVerified(Verified());

        var passkey = await Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        passkey.Name.Should().Be(PasskeyManagementService.DefaultName);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ShouldCheckThatTheCredentialIdIsUnused()
    {
        GivenCeremony(Ceremony());
        Func<byte[], CancellationToken, Task<bool>>? check = null;
        _relyingParty.Setup(r => r.VerifyRegistrationAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Func<byte[], CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Callback<string, JsonElement, Func<byte[], CancellationToken, Task<bool>>, CancellationToken>((_, _, unused, _) => check = unused)
            .ReturnsAsync(Verified());
        _passkeys.Setup(p => p.FindByCredentialIdAsync(It.Is<byte[]>(id => id[0] == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stored(5));

        await Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        (await check!([1], CancellationToken.None)).Should().BeFalse();
        (await check!([2], CancellationToken.None)).Should().BeTrue();
    }

    public static TheoryData<string, AuthSecurityReason> RefusedCeremonies => new()
    {
        { "missing", AuthSecurityReason.Expired },
        { "other-user", AuthSecurityReason.UserMismatch },
        { "other-session", AuthSecurityReason.UserMismatch },
        { "rotated", AuthSecurityReason.AuthVersionMismatch },
    };

    [Theory]
    [MemberData(nameof(RefusedCeremonies))]
    public async Task CompleteRegistrationAsync_ShouldRefuseACeremonyItWasNotStartedFor(string scenario, AuthSecurityReason reason)
    {
        var ceremony = Ceremony();
        switch (scenario)
        {
            case "missing":
                ceremony = null;
                break;
            case "other-user":
                ceremony!.UserId = 8;
                break;
            case "other-session":
                ceremony!.SessionId = "sid-2";
                break;
            case "rotated":
                _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record(authVersion: 4));
                break;
        }
        GivenCeremony(ceremony);

        var act = () => Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        await act.Should().ThrowAsync<UnauthorizedException>();
        _events.Of(AuthSecurityEvents.FactorChangeFailed).Should().ContainSingle().Which.Reason.Should().Be(reason);
        _passkeys.Verify(p => p.AddAsync(It.IsAny<PasskeyCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WebAuthnFailure.Malformed, AuthSecurityReason.InvalidRequest)]
    [InlineData(WebAuthnFailure.Rejected, AuthSecurityReason.Rejected)]
    public async Task CompleteRegistrationAsync_ShouldRefuseAResponseTheLibraryRejects(WebAuthnFailure failure, AuthSecurityReason reason)
    {
        GivenCeremony(Ceremony());
        _relyingParty.Setup(r => r.VerifyRegistrationAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Func<byte[], CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebAuthnVerificationException(failure, "detail"));

        var act = () => Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        await act.Should().ThrowAsync<UnauthorizedException>();
        _events.Of(AuthSecurityEvents.FactorChangeFailed).Single().Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData(PasskeyAddResult.CredentialTaken, typeof(UnauthorizedException))]
    [InlineData(PasskeyAddResult.LimitReached, typeof(PasskeyConflictException))]
    [InlineData(PasskeyAddResult.UserNotFound, typeof(UnauthorizedException))]
    public async Task CompleteRegistrationAsync_ShouldRefuse_WhenTheInsertIsRefused(PasskeyAddResult result, Type expected)
    {
        GivenCeremony(Ceremony());
        GivenVerified(Verified());
        _passkeys.Setup(p => p.AddAsync(It.IsAny<PasskeyCredential>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var act = () => Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        (await act.Should().ThrowAsync<AppException>()).Which.Should().BeOfType(expected);
        _factorChanges.Verify(f => f.RecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MfaFactorKind>(), It.IsAny<MfaFactorChange>()), Times.Never);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ShouldRefuseAnOversizedCredentialId()
    {
        GivenCeremony(Ceremony());
        GivenVerified(Verified() with { CredentialId = new byte[PasskeyCredential.CredentialIdMaxBytes + 1] });

        var act = () => Service().CompleteRegistrationAsync(Account, "ceremony-id", Response);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task RenameAsync_ShouldRenameAndReturnThePasskey()
    {
        _stored.Add(Stored(1));
        _passkeys.Setup(p => p.RenameAsync(UserId, 1, "Phone", It.IsAny<CancellationToken>()))
            .Callback(() => _stored[0].FriendlyName = "Phone")
            .ReturnsAsync(true);

        var renamed = await Service().RenameAsync(UserId, 1, " Phone ");

        renamed.Name.Should().Be("Phone");
    }

    [Fact]
    public async Task RenameAsync_ShouldRefuseABlankNameOrAPasskeyTheAccountDoesNotHold()
    {
        await Service().Invoking(s => s.RenameAsync(UserId, 1, "   ")).Should().ThrowAsync<BadRequestException>();
        await Service().Invoking(s => s.RenameAsync(UserId, 99, "Phone")).Should().ThrowAsync<ResourceNotFoundException>();
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemove_CancelCeremonies_AndRecordTheFactorChange()
    {
        _passkeys.Setup(p => p.RemoveIfAnotherMethodRemainsAsync(UserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasskeyRemoveResult.Removed);

        await Service().RemoveAsync(Account, 1);

        _ceremonies.Verify(c => c.RevokeAllForUserAsync(UserId));
        _factorChanges.Verify(f => f.RecordAsync(UserId, "owner@example.com", MfaFactorKind.Passkey, MfaFactorChange.Removed));
    }

    [Fact]
    public async Task RemoveAsync_ShouldRefuseTheLastSignInMethod_WithTheSharedCode()
    {
        _passkeys.Setup(p => p.RemoveIfAnotherMethodRemainsAsync(UserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasskeyRemoveResult.LastSignInMethod);

        var act = () => Service().RemoveAsync(Account, 1);

        (await act.Should().ThrowAsync<PasskeyConflictException>()).Which.ErrorCode
            .Should().Be(ProviderLinkConflictException.LastSignInMethodCode);
        _factorChanges.Verify(f => f.RecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<MfaFactorKind>(), It.IsAny<MfaFactorChange>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_ShouldReportAPasskeyTheAccountDoesNotHold()
    {
        _passkeys.Setup(p => p.RemoveIfAnotherMethodRemainsAsync(UserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasskeyRemoveResult.NotFound);

        await Service().Invoking(s => s.RemoveAsync(Account, 1)).Should().ThrowAsync<ResourceNotFoundException>();
    }

    private PasskeyManagementService Service() => new(
        _passkeys.Object,
        _users.Object,
        _links.Object,
        _ceremonies.Object,
        _relyingParty.Object,
        _factorChanges.Object,
        _events,
        new FixedTime(Now),
        Options.Create(new PasskeyOptions { MaxCredentialsPerUser = 3 }));

    private void GivenCeremony(PasskeyCeremony? ceremony) =>
        _ceremonies.Setup(c => c.ConsumeAsync("ceremony-id", PasskeyCeremonyType.Registration)).ReturnsAsync(ceremony);

    private void GivenVerified(VerifiedPasskeyRegistration verified) =>
        _relyingParty.Setup(r => r.VerifyRegistrationAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<Func<byte[], CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verified);

    private static VerifiedPasskeyRegistration Verified() =>
        new([1, 2, 3], [4, 5, 6], 0, Guid.Empty, null, "none", false, false);

    private static PasskeyCeremony Ceremony() => new()
    {
        Type = PasskeyCeremonyType.Registration,
        OptionsJson = "{}",
        UserId = UserId,
        SessionId = "sid-1",
        AuthVersion = 3,
        FriendlyName = "Laptop",
    };

    private static PasskeyCredential Stored(int id, bool backupEligible = false) => new()
    {
        Id = id,
        UserId = UserId,
        CredentialId = [(byte)id],
        PublicKey = [1],
        UserHandle = Handle,
        IsBackupEligible = backupEligible,
        FriendlyName = $"Passkey {id}",
        CreatedAtUtc = Now,
    };

    private static UserAuthRecord Record(string? password = "hash", bool disabled = false, int authVersion = 3) => new()
    {
        Id = UserId,
        Email = "owner@example.com",
        Username = "owner",
        Name = "Owner Name",
        Password = password,
        Usertype = "participant",
        IsDisabled = disabled,
        AuthVersion = authVersion,
    };

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
