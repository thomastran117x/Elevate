using backend.main.features.auth.events;
using backend.main.features.auth.history;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryCatalogTests
{
    [Fact]
    public void EveryCatalogEvent_ShouldBeEitherRecordedOrDeliberatelyNot()
    {
        // Adding a catalog event forces a decision about the owner-facing history.
        SecurityHistoryCatalog.Recorded.Should().NotIntersectWith(SecurityHistoryCatalog.NotRecorded);
        SecurityHistoryCatalog.Recorded.Union(SecurityHistoryCatalog.NotRecorded)
            .Should().BeEquivalentTo(AuthSecurityEvents.All);
    }

    public static TheoryData<AuthSecurityEventDefinition> NotRecordedEvents()
    {
        var data = new TheoryData<AuthSecurityEventDefinition>();
        foreach (var definition in SecurityHistoryCatalog.NotRecorded)
            data.Add(definition);
        return data;
    }

    [Theory]
    [MemberData(nameof(NotRecordedEvents))]
    public void Classify_ShouldIgnoreEventsThatAreNotRecorded(AuthSecurityEventDefinition definition)
    {
        SecurityHistoryCatalog.Classify(Event(definition, reason: AuthSecurityReason.InvalidCredentials))
            .Should().BeNull();
    }

    public static TheoryData<AuthSecurityEvent, AccountSecurityEventKind> RecordedCases() => new()
    {
        { Event(AuthSecurityEvents.SignInSucceeded), AccountSecurityEventKind.SignedIn },
        { Event(AuthSecurityEvents.SignInChallenged, reason: AuthSecurityReason.DeviceVerificationRequired), AccountSecurityEventKind.NewDeviceChallenge },
        { Event(AuthSecurityEvents.SignInChallenged, reason: AuthSecurityReason.StepUpRequired), AccountSecurityEventKind.SignInChallenge },
        { Event(AuthSecurityEvents.RecoveryCompleted), AccountSecurityEventKind.PasswordReset },
        { Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Password, change: AuthFactorChange.Changed), AccountSecurityEventKind.PasswordChanged },
        { Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Email, change: AuthFactorChange.Changed), AccountSecurityEventKind.EmailChanged },
        { Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Sms, change: AuthFactorChange.Enabled), AccountSecurityEventKind.MfaEnabled },
        { Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Totp, change: AuthFactorChange.Disabled), AccountSecurityEventKind.MfaDisabled },
        { Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Totp, change: AuthFactorChange.Removed), AccountSecurityEventKind.MfaRemoved },
        { Event(AuthSecurityEvents.ProviderLinked), AccountSecurityEventKind.ProviderLinked },
        { Event(AuthSecurityEvents.RefreshTokenReused, AuthSecurityOutcome.Denied, AuthSecurityReason.Replayed), AccountSecurityEventKind.RefreshTokenReuseDetected },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.Logout), AccountSecurityEventKind.SignedOut },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.PasswordChanged), AccountSecurityEventKind.SessionsRevoked },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.PasswordReset), AccountSecurityEventKind.SessionsRevoked },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.EmailChanged), AccountSecurityEventKind.SessionsRevoked },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.AccountDisabled), AccountSecurityEventKind.AccountDisabled },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.AccountEnabled), AccountSecurityEventKind.AccountEnabled },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.BindingMismatch), AccountSecurityEventKind.SessionEndedForSecurity },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.BindingMissing), AccountSecurityEventKind.SessionEndedForSecurity },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.TransportMismatch), AccountSecurityEventKind.SessionEndedForSecurity },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.UserMismatch), AccountSecurityEventKind.SessionEndedForSecurity },
        { Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.AuthVersionMismatch), AccountSecurityEventKind.SessionEndedForSecurity },
    };

    [Theory]
    [MemberData(nameof(RecordedCases))]
    public void Classify_ShouldMapRecordedEvents(AuthSecurityEvent securityEvent, AccountSecurityEventKind expected)
    {
        SecurityHistoryCatalog.Classify(securityEvent).Should().Be(expected);
    }

    public static TheoryData<AuthSecurityEvent> IgnoredCases() => new()
    {
        // No account to show it to.
        Event(AuthSecurityEvents.SignInSucceeded) with { UserId = null },
        // A fault says nothing reliable about what happened to the account.
        Event(AuthSecurityEvents.SessionRevoked, AuthSecurityOutcome.Faulted, AuthSecurityReason.Logout),
        Event(AuthSecurityEvents.SignInSucceeded, AuthSecurityOutcome.Faulted),
        // Already recorded by auth.refresh_token.reused.
        Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.TokenReuse),
        // The account is gone.
        Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.AccountDeleted),
        Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.SessionEnded),
        Event(AuthSecurityEvents.SignInChallenged, reason: AuthSecurityReason.LimitExceeded),
        Event(AuthSecurityEvents.FactorChanged, factor: AuthSecurityFactor.Sms, change: AuthFactorChange.Changed),
        Event(AuthSecurityEvents.FactorChanged),
    };

    [Theory]
    [MemberData(nameof(IgnoredCases))]
    public void Classify_ShouldIgnoreEventsWithNothingToTellTheOwner(AuthSecurityEvent securityEvent)
    {
        SecurityHistoryCatalog.Classify(securityEvent).Should().BeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(7, false)]
    [InlineData(99, true)]
    public void IsInitiatedByAdmin_ShouldOnlyFlagAnotherAccountsAction(int? actorUserId, bool expected)
    {
        var securityEvent = Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.AccountDisabled)
            with
        { ActorUserId = actorUserId };

        SecurityHistoryCatalog.IsInitiatedByAdmin(securityEvent).Should().Be(expected);
    }

    [Fact]
    public void ReasonFor_ShouldOnlyKeepRevocationReasons()
    {
        SecurityHistoryCatalog.ReasonFor(Event(AuthSecurityEvents.SessionRevoked, reason: AuthSecurityReason.PasswordChanged))
            .Should().Be("password_changed");
        SecurityHistoryCatalog.ReasonFor(Event(AuthSecurityEvents.SessionRevoked))
            .Should().BeNull();
        SecurityHistoryCatalog.ReasonFor(Event(AuthSecurityEvents.RefreshTokenReused, reason: AuthSecurityReason.Replayed))
            .Should().BeNull();
    }

    private static AuthSecurityEvent Event(
        AuthSecurityEventDefinition definition,
        AuthSecurityOutcome outcome = AuthSecurityOutcome.Succeeded,
        AuthSecurityReason reason = AuthSecurityReason.None,
        AuthSecurityFactor? factor = null,
        AuthFactorChange? change = null) => new()
        {
            Definition = definition,
            Outcome = outcome,
            Reason = reason,
            Factor = factor,
            Change = change,
            UserId = 7,
        };
}
