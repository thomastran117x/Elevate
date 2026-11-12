using System.Security.Cryptography;
using System.Text.Json;

using backend.main.features.auth.passkeys;
using backend.tests.Support;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Passkeys;

/// <summary>
/// The adapter against the real Fido2 library, fed by a software authenticator, so each check
/// the issue lists (challenge, origin, RP ID, user verification, signature, counter) is shown to
/// be enforced by the library we ship, not by a mock.
/// </summary>
public class WebAuthnRelyingPartyTests : IDisposable
{
    private const string Origin = "http://localhost:3090";

    private readonly WebAuthnRelyingParty _relyingParty = new(Options.Create(new PasskeyOptions
    {
        RelyingPartyId = "localhost",
        Origins = [Origin],
    }));

    private readonly SoftwareAuthenticator _authenticator = new(Origin);
    private readonly byte[] _userHandle = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void CreateRegistrationOptions_ShouldRequireADiscoverableUserVerifiedCredentialAndNoAttestation()
    {
        var excluded = RandomNumberGenerator.GetBytes(16);

        using var options = JsonDocument.Parse(
            _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", [excluded]));
        var root = options.RootElement;

        root.GetProperty("rp").GetProperty("id").GetString().Should().Be("localhost");
        root.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString().Should().Be("required");
        root.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString().Should().Be("required");
        root.GetProperty("attestation").GetString().Should().Be("none");
        root.GetProperty("excludeCredentials").GetArrayLength().Should().Be(1);
        root.GetProperty("challenge").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateAssertionOptions_ShouldRequireUserVerification()
    {
        using var options = JsonDocument.Parse(_relyingParty.CreateAssertionOptions([]));

        options.RootElement.GetProperty("userVerification").GetString().Should().Be("required");
        options.RootElement.GetProperty("rpId").GetString().Should().Be("localhost");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldReturnThePublicHalfOfTheNewPasskey()
    {
        _authenticator.BackupEligible = true;
        _authenticator.BackedUp = true;

        var registered = await RegisterAsync();

        registered.CredentialId.Should().Equal(_authenticator.CredentialId);
        registered.PublicKey.Should().NotBeEmpty();
        registered.IsBackupEligible.Should().BeTrue();
        registered.IsBackedUp.Should().BeTrue();
        registered.AttestationFormat.Should().Be("none");
        registered.Transports.Should().Be("internal");
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldRefuseAnotherOrigin()
    {
        _authenticator.Origin = "https://evil.example";

        var act = () => RegisterAsync();

        (await act.Should().ThrowAsync<WebAuthnVerificationException>()).Which.Reason
            .Should().Be(WebAuthnFailure.Rejected);
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldRefuseAnotherRpId()
    {
        _authenticator.RpIdOverride = "evil.example";

        var act = () => RegisterAsync();

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldRefuseAnAuthenticatorThatDidNotVerifyTheUser()
    {
        _authenticator.UserVerified = false;

        var act = () => RegisterAsync();

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldRefuseAResponseToADifferentChallenge()
    {
        var options = _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", []);
        var other = _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", []);
        var response = _authenticator.CreateAttestation(Parse(other));

        var act = () => _relyingParty.VerifyRegistrationAsync(options, response, Unused);

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyRegistrationAsync_ShouldRefuseACredentialIdAlreadyInUse()
    {
        var options = _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", []);
        var response = _authenticator.CreateAttestation(Parse(options));

        var act = () => _relyingParty.VerifyRegistrationAsync(
            options, response, (_, _) => Task.FromResult(false));

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Theory]
    [InlineData("\"not an object\"")]
    [InlineData("{\"id\":5}")]
    [InlineData("{\"id\":\"AA\",\"rawId\":\"%%%\",\"type\":\"public-key\",\"response\":{}}")]
    public async Task VerifyRegistrationAsync_ShouldReportMalformedInputAsMalformed(string json)
    {
        var options = _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", []);

        var act = () => _relyingParty.VerifyRegistrationAsync(options, Parse(json), Unused);

        (await act.Should().ThrowAsync<WebAuthnVerificationException>()).Which.Reason
            .Should().BeOneOf(WebAuthnFailure.Malformed, WebAuthnFailure.Rejected);
    }

    [Fact]
    public async Task ReadAssertion_ShouldNameTheCredentialUserHandleAndBackupEligibility()
    {
        _authenticator.BackupEligible = true;
        await RegisterAsync();

        var candidate = _relyingParty.ReadAssertion(
            _authenticator.CreateAssertion(Parse(_relyingParty.CreateAssertionOptions([]))));

        candidate.CredentialId.Should().Equal(_authenticator.CredentialId);
        candidate.UserHandle.Should().Equal(_userHandle);
        candidate.IsBackupEligible.Should().BeTrue();
    }

    [Fact]
    public void ReadAssertion_ShouldReportGarbageAsMalformed()
    {
        var act = () => _relyingParty.ReadAssertion(Parse("[1,2,3]"));

        act.Should().Throw<WebAuthnVerificationException>().Which.Reason.Should().Be(WebAuthnFailure.Malformed);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldAcceptAValidAssertionAndReportTheCounter()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([stored.CredentialId]);

        var verified = await _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: true);

        verified.SignatureCounter.Should().Be(1);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldRefuseABadSignature()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([]);

        var act = () => _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options), corruptSignature: true), stored, enforceCounter: true);

        (await act.Should().ThrowAsync<WebAuthnVerificationException>()).Which.Reason
            .Should().Be(WebAuthnFailure.Rejected);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldRefuseAnotherOrigin()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([]);
        _authenticator.Origin = "https://lookalike.example";

        var act = () => _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: true);

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldRefuseAnAssertionWithoutUserVerification()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([]);
        _authenticator.UserVerified = false;

        var act = () => _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: true);

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldRefuseAUserHandleThatIsNotTheStoredOne()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([]);

        var act = () => _relyingParty.VerifyAssertionAsync(
            options,
            _authenticator.CreateAssertion(Parse(options), userHandleOverride: RandomNumberGenerator.GetBytes(32)),
            stored,
            enforceCounter: true);

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldReportACounterRegression_WhenEnforcingTheCounter()
    {
        var stored = await StoredAsync();
        stored.SignatureCounter = 10;
        var options = _relyingParty.CreateAssertionOptions([]);

        var act = () => _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: true);

        (await act.Should().ThrowAsync<WebAuthnVerificationException>()).Which.Reason
            .Should().Be(WebAuthnFailure.CounterRegressed);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldLeaveTheCounterToTheCaller_WhenNotEnforcingIt()
    {
        var stored = await StoredAsync();
        stored.SignatureCounter = 10;
        var options = _relyingParty.CreateAssertionOptions([]);

        var verified = await _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: false);

        verified.SignatureCounter.Should().Be(1);
    }

    [Fact]
    public async Task VerifyAssertionAsync_ShouldRefuseACredentialOutsideTheAllowList()
    {
        var stored = await StoredAsync();
        var options = _relyingParty.CreateAssertionOptions([RandomNumberGenerator.GetBytes(16)]);

        var act = () => _relyingParty.VerifyAssertionAsync(
            options, _authenticator.CreateAssertion(Parse(options)), stored, enforceCounter: true);

        await act.Should().ThrowAsync<WebAuthnVerificationException>();
    }

    public void Dispose() => _authenticator.Dispose();

    private async Task<VerifiedPasskeyRegistration> RegisterAsync()
    {
        var options = _relyingParty.CreateRegistrationOptions(_userHandle, "owner", "Owner", []);
        return await _relyingParty.VerifyRegistrationAsync(
            options, _authenticator.CreateAttestation(Parse(options)), Unused);
    }

    private async Task<PasskeyCredential> StoredAsync()
    {
        var registered = await RegisterAsync();
        return new PasskeyCredential
        {
            Id = 1,
            UserId = 7,
            CredentialId = registered.CredentialId,
            PublicKey = registered.PublicKey,
            UserHandle = _userHandle,
            SignatureCounter = registered.SignatureCounter,
            IsBackupEligible = registered.IsBackupEligible,
            FriendlyName = "Passkey",
        };
    }

    private static Task<bool> Unused(byte[] credentialId, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
