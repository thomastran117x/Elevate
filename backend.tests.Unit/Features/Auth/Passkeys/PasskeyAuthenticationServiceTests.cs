using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.passkeys;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class PasskeyAuthenticationServiceTests
{
    private const int UserId = 7;
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Handle = [1, 2, 3, 4];
    private static readonly byte[] CredentialId = [9, 9, 9];
    private static readonly PasskeyAccount Account = new(UserId, "owner@example.com", "sid-1", 3);
    private static readonly JsonElement Response = JsonDocument.Parse("{}").RootElement;

    private readonly Mock<IPasskeyCredentialRepository> _passkeys = new();
    private readonly Mock<IAuthUserRepository> _users = new();
    private readonly Mock<IPasskeyCeremonyStore> _ceremonies = new();
    private readonly Mock<IWebAuthnRelyingParty> _relyingParty = new();
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly Mock<ISessionMfaVerificationService> _stepUp = new();
    private readonly RecordingAuthSecurityEventEmitter _events = new();
    private PasskeyCredential _stored = Stored();

    public PasskeyAuthenticationServiceTests()
    {
        _ceremonies.Setup(c => c.CreateAsync(It.IsAny<PasskeyCeremony>())).ReturnsAsync("ceremony-id");
        _relyingParty.Setup(r => r.CreateAssertionOptions(It.IsAny<IReadOnlyList<byte[]>>())).Returns("{\"challenge\":\"abc\"}");
        _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
            .Returns(() => new PasskeyAssertionCandidate(CredentialId, Handle, _stored.IsBackupEligible));
        _relyingParty.Setup(r => r.VerifyAssertionAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<PasskeyCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifiedPasskeyAssertion(6, false));
        _passkeys.Setup(p => p.FindByCredentialIdAsync(CredentialId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _stored);
        _passkeys.Setup(p => p.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => [_stored]);
        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record());
        _sessions.Setup(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<SessionTransport>(), null, It.IsAny<bool?>()))
            .ReturnsAsync((User user, SessionTransport transport, string? _, bool? _) =>
                new UserToken(new Token("access", Now.AddMinutes(15), "refresh", "binding", TimeSpan.FromDays(1), transport), user));
        _stepUp.Setup(s => s.CaptureSnapshotAsync(UserId)).ReturnsAsync(new StepUpSnapshot(4, "state"));
    }

    [Fact]
    public async Task StartSignInAsync_ShouldOfferAnyDiscoverablePasskey()
    {
        PasskeyCeremony? created = null;
        _ceremonies.Setup(c => c.CreateAsync(It.IsAny<PasskeyCeremony>()))
            .Callback<PasskeyCeremony>(c => created = c)
            .ReturnsAsync("ceremony-id");

        var options = await Service().StartSignInAsync();

        options.CeremonyId.Should().Be("ceremony-id");
        created!.Type.Should().Be(PasskeyCeremonyType.SignIn);
        created.UserId.Should().BeNull();
        _relyingParty.Verify(r => r.CreateAssertionOptions(It.Is<IReadOnlyList<byte[]>>(ids => ids.Count == 0)));
    }

    [Fact]
    public async Task CompleteSignInAsync_ShouldIssueASession_AndRecordAPasskeySignIn()
    {
        GivenCeremony(PasskeyCeremonyType.SignIn, SignInCeremony());

        var token = await Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.BrowserCookie, rememberMe: true);

        token.user.Id.Should().Be(UserId);
        token.user.AuthVersion.Should().Be(3);
        _sessions.Verify(s => s.IssueAsync(It.Is<User>(u => u.Id == UserId), SessionTransport.BrowserCookie, null, true));
        _events.Of(AuthSecurityEvents.SignInSucceeded).Single().Method.Should().Be(AuthSecurityMethod.Passkey);
        _passkeys.Verify(p => p.RecordUseAsync(_stored.Id, 6, false, Now, false, It.IsAny<CancellationToken>()));
        _relyingParty.Verify(r => r.VerifyAssertionAsync(
            "{}", It.IsAny<JsonElement>(), _stored, true, It.IsAny<CancellationToken>()));
    }

    public static TheoryData<string, AuthSecurityReason> RefusedSignIns => new()
    {
        { "spent-ceremony", AuthSecurityReason.Expired },
        { "malformed", AuthSecurityReason.InvalidRequest },
        { "no-user-handle", AuthSecurityReason.InvalidRequest },
        { "unknown-credential", AuthSecurityReason.CredentialNotFound },
        { "handle-mismatch", AuthSecurityReason.UserMismatch },
        { "account-gone", AuthSecurityReason.CredentialNotFound },
        { "bad-signature", AuthSecurityReason.Rejected },
        { "garbled", AuthSecurityReason.InvalidRequest },
        { "backup-flag-changed", AuthSecurityReason.Rejected },
    };

    /// <summary>Every refusal is the same 401 to the client; only the security event tells them apart.</summary>
    [Theory]
    [MemberData(nameof(RefusedSignIns))]
    public async Task CompleteSignInAsync_ShouldRefuseGenerically(string scenario, AuthSecurityReason reason)
    {
        GivenCeremony(PasskeyCeremonyType.SignIn, scenario == "spent-ceremony" ? null : SignInCeremony());
        switch (scenario)
        {
            case "malformed":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Throws(new WebAuthnVerificationException(WebAuthnFailure.Malformed, "bad"));
                break;
            case "no-user-handle":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Returns(new PasskeyAssertionCandidate(CredentialId, null, false));
                break;
            case "unknown-credential":
                _passkeys.Setup(p => p.FindByCredentialIdAsync(CredentialId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PasskeyCredential?)null);
                break;
            case "handle-mismatch":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Returns(new PasskeyAssertionCandidate(CredentialId, [7, 7], false));
                break;
            case "account-gone":
                _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync((UserAuthRecord?)null);
                break;
            case "bad-signature":
                GivenVerificationFails(WebAuthnFailure.Rejected);
                break;
            case "garbled":
                GivenVerificationFails(WebAuthnFailure.Malformed);
                break;
            case "backup-flag-changed":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Returns(new PasskeyAssertionCandidate(CredentialId, Handle, true));
                break;
        }

        var act = () => Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.BrowserCookie, false);

        (await act.Should().ThrowAsync<PasskeyAuthenticationFailedException>()).Which.StatusCode.Should().Be(401);
        var failed = _events.Of(AuthSecurityEvents.SignInFailed).Should().ContainSingle().Subject;
        failed.Reason.Should().Be(reason);
        failed.Method.Should().Be(AuthSecurityMethod.Passkey);
        _sessions.Verify(s => s.IssueAsync(It.IsAny<User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()), Times.Never);
    }

    [Fact]
    public async Task CompleteSignInAsync_ShouldRefuseADisabledAccount_OnlyAfterThePasskeyIsProven()
    {
        GivenCeremony(PasskeyCeremonyType.SignIn, SignInCeremony());
        _users.Setup(u => u.GetAuthByIdAsync(UserId)).ReturnsAsync(Record(disabled: true));

        var act = () => Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.ApiToken, false);

        await act.Should().ThrowAsync<ForbiddenException>();
        _events.Of(AuthSecurityEvents.SignInFailed).Single().Reason.Should().Be(AuthSecurityReason.AccountDisabled);
        _relyingParty.Verify(r => r.VerifyAssertionAsync(
            It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<PasskeyCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CompleteSignInAsync_ShouldRefuseADeviceBoundCounterRegression_AndFlagThePasskey()
    {
        GivenCeremony(PasskeyCeremonyType.SignIn, SignInCeremony());
        GivenVerificationFails(WebAuthnFailure.CounterRegressed);

        var act = () => Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.BrowserCookie, false);

        await act.Should().ThrowAsync<PasskeyAuthenticationFailedException>();
        _passkeys.Verify(p => p.FlagCounterAnomalyAsync(_stored.Id, Now, It.IsAny<CancellationToken>()));
        _events.Of(AuthSecurityEvents.PasskeyCounterAnomaly).Single().Outcome.Should().Be(AuthSecurityOutcome.Denied);
        _events.Of(AuthSecurityEvents.SignInFailed).Single().Reason.Should().Be(AuthSecurityReason.CounterRegressed);
    }

    /// <summary>A synced passkey's copies share one credential, so its counter may not advance.</summary>
    [Fact]
    public async Task CompleteSignInAsync_ShouldAcceptASyncedPasskeyWhoseCounterDidNotAdvance_ButRecordIt()
    {
        _stored = Stored(backupEligible: true, counter: 10);
        GivenCeremony(PasskeyCeremonyType.SignIn, SignInCeremony());
        _relyingParty.Setup(r => r.VerifyAssertionAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<PasskeyCredential>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifiedPasskeyAssertion(3, true));

        await Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.BrowserCookie, false);

        _events.Of(AuthSecurityEvents.PasskeyCounterAnomaly).Single().Outcome.Should().Be(AuthSecurityOutcome.Succeeded);
        _passkeys.Verify(p => p.RecordUseAsync(_stored.Id, 3, true, Now, true, It.IsAny<CancellationToken>()));
        _events.Of(AuthSecurityEvents.SignInSucceeded).Should().ContainSingle();
    }

    [Fact]
    public async Task CompleteSignInAsync_ShouldNotFlagASyncedPasskeyThatNeverCounts()
    {
        _stored = Stored(backupEligible: true, counter: 0);
        GivenCeremony(PasskeyCeremonyType.SignIn, SignInCeremony());
        _relyingParty.Setup(r => r.VerifyAssertionAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<PasskeyCredential>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifiedPasskeyAssertion(0, true));

        await Service().CompleteSignInAsync("ceremony-id", Response, SessionTransport.BrowserCookie, false);

        _events.Of(AuthSecurityEvents.PasskeyCounterAnomaly).Should().BeEmpty();
        _passkeys.Verify(p => p.RecordUseAsync(_stored.Id, 0, true, Now, false, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task StartStepUpAsync_ShouldOfferOnlyTheCallersPasskeys_AndCaptureTheStepUpState()
    {
        PasskeyCeremony? created = null;
        _ceremonies.Setup(c => c.CreateAsync(It.IsAny<PasskeyCeremony>()))
            .Callback<PasskeyCeremony>(c => created = c)
            .ReturnsAsync("ceremony-id");

        await Service().StartStepUpAsync(Account);

        _relyingParty.Verify(r => r.CreateAssertionOptions(
            It.Is<IReadOnlyList<byte[]>>(ids => ids.Count == 1 && ids[0].SequenceEqual(CredentialId))));
        created!.Type.Should().Be(PasskeyCeremonyType.StepUp);
        created.UserId.Should().Be(UserId);
        created.SessionId.Should().Be("sid-1");
        created.StepUpGeneration.Should().Be(4);
        created.FactorState.Should().Be("state");
    }

    [Fact]
    public async Task StartStepUpAsync_ShouldRefuse_WithoutAPasskey()
    {
        _passkeys.Setup(p => p.ListForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Service().Invoking(s => s.StartStepUpAsync(Account)).Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task CompleteStepUpAsync_ShouldRecordAProofAgainstTheCapturedState()
    {
        GivenCeremony(PasskeyCeremonyType.StepUp, StepUpCeremony());

        await Service().CompleteStepUpAsync(Account, "ceremony-id", Response);

        _stepUp.Verify(s => s.RecordPasskeyProofAsync(UserId, "sid-1", 3, new StepUpSnapshot(4, "state")));
        _passkeys.Verify(p => p.RecordUseAsync(_stored.Id, 6, false, Now, false, It.IsAny<CancellationToken>()));
    }

    public static TheoryData<string, AuthSecurityReason> RefusedStepUps => new()
    {
        { "spent-ceremony", AuthSecurityReason.Expired },
        { "other-session", AuthSecurityReason.UserMismatch },
        { "other-user", AuthSecurityReason.UserMismatch },
        { "rotated", AuthSecurityReason.AuthVersionMismatch },
        { "no-snapshot", AuthSecurityReason.AuthVersionMismatch },
        { "malformed", AuthSecurityReason.InvalidRequest },
        { "unknown-credential", AuthSecurityReason.CredentialNotFound },
        { "another-accounts-passkey", AuthSecurityReason.UserMismatch },
        { "handle-mismatch", AuthSecurityReason.UserMismatch },
        { "bad-signature", AuthSecurityReason.Rejected },
    };

    [Theory]
    [MemberData(nameof(RefusedStepUps))]
    public async Task CompleteStepUpAsync_ShouldRefuseGenerically(string scenario, AuthSecurityReason reason)
    {
        var ceremony = StepUpCeremony();
        switch (scenario)
        {
            case "spent-ceremony":
                ceremony = null;
                break;
            case "other-session":
                ceremony!.SessionId = "sid-2";
                break;
            case "other-user":
                ceremony!.UserId = 8;
                break;
            case "rotated":
                ceremony!.AuthVersion = 2;
                break;
            case "no-snapshot":
                ceremony!.FactorState = null;
                break;
            case "malformed":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Throws(new WebAuthnVerificationException(WebAuthnFailure.Malformed, "bad"));
                break;
            case "unknown-credential":
                _passkeys.Setup(p => p.FindByCredentialIdAsync(CredentialId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PasskeyCredential?)null);
                break;
            case "another-accounts-passkey":
                _stored = Stored(userId: 8);
                break;
            case "handle-mismatch":
                _relyingParty.Setup(r => r.ReadAssertion(It.IsAny<JsonElement>()))
                    .Returns(new PasskeyAssertionCandidate(CredentialId, [7, 7], false));
                break;
            case "bad-signature":
                GivenVerificationFails(WebAuthnFailure.Rejected);
                break;
        }
        GivenCeremony(PasskeyCeremonyType.StepUp, ceremony);

        var act = () => Service().CompleteStepUpAsync(Account, "ceremony-id", Response);

        await act.Should().ThrowAsync<PasskeyAuthenticationFailedException>();
        var failed = _events.Of(AuthSecurityEvents.StepUpFailed).Should().ContainSingle().Subject;
        failed.Reason.Should().Be(reason);
        failed.Flow.Should().Be(AuthSecurityFlow.SessionStepUp);
        _stepUp.Verify(s => s.RecordPasskeyProofAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StepUpSnapshot>()), Times.Never);
    }

    private PasskeyAuthenticationService Service() => new(
        _passkeys.Object,
        _users.Object,
        _ceremonies.Object,
        _relyingParty.Object,
        _sessions.Object,
        _stepUp.Object,
        _events,
        new FixedTime(Now));

    private void GivenCeremony(PasskeyCeremonyType type, PasskeyCeremony? ceremony) =>
        _ceremonies.Setup(c => c.ConsumeAsync("ceremony-id", type)).ReturnsAsync(ceremony);

    private void GivenVerificationFails(WebAuthnFailure failure) =>
        _relyingParty.Setup(r => r.VerifyAssertionAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<PasskeyCredential>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebAuthnVerificationException(failure, "detail"));

    private static PasskeyCeremony SignInCeremony() => new() { Type = PasskeyCeremonyType.SignIn, OptionsJson = "{}" };

    private static PasskeyCeremony StepUpCeremony() => new()
    {
        Type = PasskeyCeremonyType.StepUp,
        OptionsJson = "{}",
        UserId = UserId,
        SessionId = "sid-1",
        AuthVersion = 3,
        StepUpGeneration = 4,
        FactorState = "state",
    };

    private static PasskeyCredential Stored(bool backupEligible = false, long counter = 5, int userId = UserId) => new()
    {
        Id = 11,
        UserId = userId,
        CredentialId = CredentialId,
        PublicKey = [1],
        UserHandle = Handle,
        SignatureCounter = counter,
        IsBackupEligible = backupEligible,
        FriendlyName = "Passkey",
    };

    private static UserAuthRecord Record(bool disabled = false) => new()
    {
        Id = UserId,
        Email = "owner@example.com",
        Username = "owner",
        Password = null,
        Usertype = "participant",
        IsDisabled = disabled,
        AuthVersion = 3,
    };

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
