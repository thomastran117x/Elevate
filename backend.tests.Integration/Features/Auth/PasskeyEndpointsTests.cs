using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.shared.providers.messages;
using backend.tests.Integration.Infrastructure;
using backend.tests.Support;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Passkeys end to end: a software authenticator answers the API's real options, and the real
/// server library verifies what it signs, against Postgres and Redis.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PasskeyEndpointsTests
{
    private const string Origin = "http://localhost:3090";

    [Fact]
    public async Task Passkey_ShouldRegisterSignInStepUpRenameAndRemove()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-journey");
        using var authenticator = new SoftwareAuthenticator(Origin);

        var registered = await RegisterAsync(app, session.AccessToken, authenticator, "Work laptop");
        registered.Name.Should().Be("Work laptop");
        registered.CanRemove.Should().BeTrue("the account still has its password");

        var stored = await app.QueryDbAsync(db => db.PasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId));
        stored.CredentialId.Should().Equal(authenticator.CredentialId);
        stored.UserHandle.Should().Equal(authenticator.UserHandle);
        (await app.QueryDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.PasskeyUserHandle).SingleAsync()))
            .Should().Equal(authenticator.UserHandle);
        var notice = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.MfaFactorChanged && m.Email == "passkey-journey@example.com" && m.MfaFactor == "passkey");
        notice.MfaChange.Should().Be("added");

        // Sign in with the passkey alone: no username, no password, no further challenge.
        var signIn = await SignInAsync(app, authenticator, transport: SessionTransportResolver.ApiValue);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(signIn));
        var signedIn = (await app.ReadApiResponseAsync<LoginAuthenticationResponse>(signIn)).Data!;
        signedIn.Type.Should().Be("authenticated");
        var passkeySession = signedIn.Auth!;
        passkeySession.RefreshToken.Should().NotBeNullOrWhiteSpace();
        (await app.GetWithBearerAsync("/api/auth/me", passkeySession.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.QueryDbAsync(db => db.PasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId)))
            .LastUsedAtUtc.Should().NotBeNull();

        // The new session has no step-up yet; the passkey provides one.
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", passkeySession.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var methods = await app.GetWithBearerAsync("/api/auth/mfa/step-up/options", passkeySession.AccessToken);
        (await app.ReadApiResponseAsync<SessionMfaOptionsResponse>(methods)).Data!.AvailableMethods.First()
            .Should().Be("passkey");
        var stepUp = await StepUpAsync(app, passkeySession.AccessToken, authenticator);
        stepUp.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(stepUp));
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", passkeySession.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var rename = await SendAsync(
            app,
            new HttpRequestMessage(HttpMethod.Patch, $"/api/auth/passkeys/{registered.Id}") { Content = JsonContent.Create(new { name = "Phone" }) },
            passkeySession.AccessToken);
        rename.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(rename));
        var listed = await app.GetWithBearerAsync("/api/auth/passkeys", passkeySession.AccessToken);
        var passkeys = (await app.ReadApiResponseAsync<PasskeysResponse>(listed)).Data!.Passkeys;
        passkeys.Should().ContainSingle().Which.Name.Should().Be("Phone");
        (await listed.Content.ReadAsStringAsync()).Should().NotContain(Convert.ToBase64String(stored.PublicKey));

        var remove = await SendAsync(
            app, new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/passkeys/{registered.Id}"), passkeySession.AccessToken);
        remove.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(remove));
        (await app.QueryDbAsync(db => db.PasskeyCredentials.CountAsync(c => c.UserId == userId))).Should().Be(0);

        // A removed passkey signs no one in.
        (await SignInAsync(app, authenticator)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registration_ShouldRequireAFreshStepUp_AndACsrfToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, _) = await SignUpAsync(app, "passkey-gate");

        var gated = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/registration/options", new { }, session.AccessToken);
        gated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gated.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");

        await app.GrantStepUpProofAsync(session.AccessToken);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/passkeys/registration/options")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var withoutCsrf = await app.Client.SendAsync(request);
        withoutCsrf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withoutCsrf.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");
    }

    [Fact]
    public async Task SignIn_ShouldRequireACsrfToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.PostAsJsonAsync("/api/auth/passkeys/sign-in/options", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A browser fetches its CSRF token before restoring a session, so a signed-in visit to the
    /// login page sends a bearer token with an anonymously issued CSRF token. That pair is valid.
    /// </summary>
    [Fact]
    public async Task SignInOptions_ShouldAcceptAnAnonymousCsrfToken_FromASignedInBrowser()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, _) = await SignUpAsync(app, "passkey-signed-in-csrf");

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/passkeys/sign-in/options", new { }, session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
    }

    [Fact]
    public async Task SignIn_ShouldRefuseAReplayedCeremony()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, _) = await SignUpAsync(app, "passkey-replay");
        using var authenticator = new SoftwareAuthenticator(Origin);
        await RegisterAsync(app, session.AccessToken, authenticator);

        var options = await SignInOptionsAsync(app);
        var payload = new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAssertion(options.PublicKey),
            transport = SessionTransportResolver.ApiValue,
        };

        var first = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", payload);
        var replay = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", payload);

        first.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(first));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("AUTHENTICATION_FAILED");
    }

    [Fact]
    public async Task SignIn_ShouldRefuseAnExpiredCeremony()
    {
        var clock = new AdvanceableTimeProvider();
        await using var app = await AuthApiTestApp.CreateAsync(serviceOverrides: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var (session, _) = await SignUpAsync(app, "passkey-expired");
        using var authenticator = new SoftwareAuthenticator(Origin);
        await RegisterAsync(app, session.AccessToken, authenticator);

        var options = await SignInOptionsAsync(app);
        clock.Advance(TimeSpan.FromMinutes(6));

        var response = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAssertion(options.PublicKey),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("rp-id")]
    [InlineData("no-user-verification")]
    [InlineData("signature")]
    [InlineData("challenge")]
    [InlineData("malformed")]
    public async Task SignIn_ShouldRefuseGenerically(string defect)
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, $"passkey-refuse-{defect}");
        using var authenticator = new SoftwareAuthenticator(Origin);
        await RegisterAsync(app, session.AccessToken, authenticator);

        var options = await SignInOptionsAsync(app);
        object credential;
        switch (defect)
        {
            case "origin":
                authenticator.Origin = "https://lookalike.example";
                credential = authenticator.CreateAssertion(options.PublicKey);
                break;
            case "rp-id":
                authenticator.RpIdOverride = "lookalike.example";
                credential = authenticator.CreateAssertion(options.PublicKey);
                break;
            case "no-user-verification":
                authenticator.UserVerified = false;
                credential = authenticator.CreateAssertion(options.PublicKey);
                break;
            case "signature":
                credential = authenticator.CreateAssertion(options.PublicKey, corruptSignature: true);
                break;
            case "challenge":
                var other = await SignInOptionsAsync(app);
                credential = authenticator.CreateAssertion(other.PublicKey);
                break;
            default:
                credential = new { id = "x", rawId = "%%", type = "public-key", response = new { } };
                break;
        }

        var response = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", new
        {
            ceremonyId = options.CeremonyId,
            credential,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("AUTHENTICATION_FAILED").And.Contain("Passkey verification failed.");
        (await app.QueryDbAsync(db => db.PasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId)))
            .LastUsedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task SignIn_ShouldRefuseADisabledAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-disabled");
        using var authenticator = new SoftwareAuthenticator(Origin);
        await RegisterAsync(app, session.AccessToken, authenticator);
        await app.QueryDbAsync(db => db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsDisabled, true)));

        var response = await SignInAsync(app, authenticator);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SignIn_ShouldRefuseADeviceBoundPasskeyWhoseCounterWentBack()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-cloned");
        using var authenticator = new SoftwareAuthenticator(Origin) { Counter = 5 };
        await RegisterAsync(app, session.AccessToken, authenticator);
        (await SignInAsync(app, authenticator)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A copy of the authenticator that is behind the original.
        authenticator.Counter = 1;
        var cloned = await SignInAsync(app, authenticator);

        cloned.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.QueryDbAsync(db => db.PasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId)))
            .CounterAnomalyAtUtc.Should().NotBeNull();
    }

    /// <summary>Synced passkeys report no meaningful counter; they must keep working.</summary>
    [Fact]
    public async Task SignIn_ShouldKeepAcceptingASyncedPasskeyThatNeverCounts()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-synced");
        using var authenticator = new SoftwareAuthenticator(Origin) { BackupEligible = true, BackedUp = true, CounterStep = 0 };
        await RegisterAsync(app, session.AccessToken, authenticator);

        (await SignInAsync(app, authenticator)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SignInAsync(app, authenticator)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await app.QueryDbAsync(db => db.PasskeyCredentials.AsNoTracking().SingleAsync(c => c.UserId == userId));
        stored.IsBackupEligible.Should().BeTrue();
        stored.CounterAnomalyAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Passkeys_ShouldNotWorkForAnotherAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (owner, _) = await SignUpAsync(app, "passkey-owner");
        var (intruder, intruderId) = await SignUpAsync(app, "passkey-intruder");
        using var ownersKey = new SoftwareAuthenticator(Origin);
        using var intrudersKey = new SoftwareAuthenticator(Origin);
        await RegisterAsync(app, owner.AccessToken, ownersKey);
        await RegisterAsync(app, intruder.AccessToken, intrudersKey);

        // The owner's passkey cannot step up the intruder's session.
        var options = await StepUpOptionsAsync(app, intruder.AccessToken);
        var stepUp = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/step-up", new
        {
            ceremonyId = options.CeremonyId,
            credential = ownersKey.CreateAssertion(options.PublicKey),
        }, intruder.AccessToken);
        stepUp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Nor can the owner's credential id be registered to the intruder's account.
        await app.GrantStepUpProofAsync(intruder.AccessToken);
        var registration = await RegistrationOptionsAsync(app, intruder.AccessToken);
        var copy = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/registration", new
        {
            ceremonyId = registration.CeremonyId,
            credential = ownersKey.CreateAttestation(registration.PublicKey),
        }, intruder.AccessToken);
        copy.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.QueryDbAsync(db => db.PasskeyCredentials.CountAsync(c => c.UserId == intruderId))).Should().Be(1);

        // And a user handle swapped for the intruder's does not sign the owner's passkey in as them.
        var signIn = await SignInOptionsAsync(app);
        var swapped = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", new
        {
            ceremonyId = signIn.CeremonyId,
            credential = ownersKey.CreateAssertion(signIn.PublicKey, userHandleOverride: intrudersKey.UserHandle),
        });
        swapped.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registration_ShouldBeRefused_WhenTheAccountsCredentialsRotateMidCeremony()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-rotated");
        using var authenticator = new SoftwareAuthenticator(Origin);
        await app.GrantStepUpProofAsync(session.AccessToken);
        var options = await RegistrationOptionsAsync(app, session.AccessToken);

        await app.QueryDbAsync(db => db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.AuthVersion, u => u.AuthVersion + 1)));
        var response = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/registration", new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAttestation(options.PublicKey),
        }, session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.QueryDbAsync(db => db.PasskeyCredentials.CountAsync(c => c.UserId == userId))).Should().Be(0);
    }

    [Fact]
    public async Task Remove_ShouldRefuseTheLastWayToSignIn_EvenWhenRemovalsRace()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpAsync(app, "passkey-last");
        using var first = new SoftwareAuthenticator(Origin);
        using var second = new SoftwareAuthenticator(Origin);
        var one = await RegisterAsync(app, session.AccessToken, first);
        var two = await RegisterAsync(app, session.AccessToken, second);
        await app.QueryDbAsync(db => db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.Password, (string?)null)));
        await app.GrantStepUpProofAsync(session.AccessToken);

        var results = await Task.WhenAll(
            SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/passkeys/{one.Id}"), session.AccessToken),
            SendAsync(app, new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/passkeys/{two.Id}"), session.AccessToken));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var refused = results.Single(r => r.StatusCode != HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("LAST_SIGN_IN_METHOD");
        (await app.QueryDbAsync(db => db.PasskeyCredentials.CountAsync(c => c.UserId == userId))).Should().Be(1);
    }

    [Fact]
    public async Task Passkeys_ShouldBeAbsent_WhenTheFeatureIsOff()
    {
        await using var app = await AuthApiTestApp.CreateAsync(configurationOverrides: new Dictionary<string, string?>
        {
            ["FeatureFlags:auth.passkeys"] = "false",
        });
        var (session, _) = await SignUpAsync(app, "passkey-off");

        (await app.GetWithBearerAsync("/api/auth/passkeys", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<(AuthenticatedSessionResponse Session, int UserId)> SignUpAsync(AuthApiTestApp app, string username)
    {
        var email = $"{username}@example.com";
        var session = await app.SignUpAndVerifyByTokenAsync(email, transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync(email);
        return (session, user!.Id);
    }

    private static async Task<PasskeyResponse> RegisterAsync(
        AuthApiTestApp app,
        string accessToken,
        SoftwareAuthenticator authenticator,
        string? name = null)
    {
        // Each registration is a factor change that spends the proof, so each one steps up.
        await app.GrantStepUpProofAsync(accessToken);
        var options = await RegistrationOptionsAsync(app, accessToken, name);
        var response = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/registration", new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAttestation(options.PublicKey),
        }, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        await app.GrantStepUpProofAsync(accessToken);
        return (await app.ReadApiResponseAsync<PasskeyResponse>(response)).Data!;
    }

    private static async Task<PasskeyCeremonyOptionsResponse> RegistrationOptionsAsync(
        AuthApiTestApp app,
        string accessToken,
        string? name = null)
    {
        var response = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/registration/options", new { name }, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<PasskeyCeremonyOptionsResponse>(response)).Data!;
    }

    private static async Task<PasskeyCeremonyOptionsResponse> SignInOptionsAsync(AuthApiTestApp app)
    {
        var response = await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in/options", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<PasskeyCeremonyOptionsResponse>(response)).Data!;
    }

    private static async Task<HttpResponseMessage> SignInAsync(
        AuthApiTestApp app,
        SoftwareAuthenticator authenticator,
        string? transport = null)
    {
        var options = await SignInOptionsAsync(app);
        return await app.PostJsonWithCsrfAsync("/api/auth/passkeys/sign-in", new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAssertion(options.PublicKey),
            transport = transport ?? SessionTransportResolver.ApiValue,
        });
    }

    private static async Task<PasskeyCeremonyOptionsResponse> StepUpOptionsAsync(AuthApiTestApp app, string accessToken)
    {
        var response = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/step-up/options", new { }, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<PasskeyCeremonyOptionsResponse>(response)).Data!;
    }

    private static async Task<HttpResponseMessage> StepUpAsync(
        AuthApiTestApp app,
        string accessToken,
        SoftwareAuthenticator authenticator)
    {
        var options = await StepUpOptionsAsync(app, accessToken);
        return await app.PostJsonWithBearerAndCsrfAsync("/api/auth/passkeys/step-up", new
        {
            ceremonyId = options.CeremonyId,
            credential = authenticator.CreateAssertion(options.PublicKey),
        }, accessToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        AuthApiTestApp app,
        HttpRequestMessage request,
        string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private sealed class AdvanceableTimeProvider : TimeProvider
    {
        private TimeSpan _offset;

        public void Advance(TimeSpan by) => _offset += by;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;
    }
}
