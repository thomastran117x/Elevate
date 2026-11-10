extern alias securityhistoryworker;

using System.Net;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.history;
using backend.main.features.auth.oauth;
using backend.main.features.auth.oauth.links;
using backend.main.features.auth.token;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using securityhistoryworker::backend.worker.security_history_worker;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// End-to-end coverage for explicit provider linking (#329): server-held state, nonce, and PKCE;
/// ownership and session binding; step-up; uniqueness under concurrency; and last-method
/// protection on unlink.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ProviderLinkEndpointsTests
{
    [Fact]
    public async Task List_ShouldReportThePasswordAndEveryProvider()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-list");

        var response = await app.GetWithBearerAsync("/api/auth/providers", session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await app.ReadApiResponseAsync<ProviderLinksResponse>(response)).Data!;
        data.HasPassword.Should().BeTrue();
        data.Providers.Select(p => p.Provider).Should().Equal("google", "microsoft");
        data.Providers.Should().OnlyContain(p => p.Available && p.Link == null && !p.CanUnlink);
    }

    [Fact]
    public async Task List_ShouldRequireAuthentication()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        (await app.Client.GetAsync("/api/auth/providers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Start_ShouldRequireACsrfToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-csrf");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/providers/google/link/start");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CSRF_VALIDATION_FAILED");
    }

    /// <summary>
    /// The server issues the state, nonce, and PKCE verifier, redeems the Google code itself with
    /// the verifier and redirect it kept, and accepts only a token carrying its nonce.
    /// </summary>
    [Fact]
    public async Task GoogleLink_ShouldCompleteWithTheServersOwnStateNonceAndVerifier()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-google");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var start = await StartAsync(app, session, "google");
        start.CodeVerifier.Should().BeNull("Google's verifier never leaves the server");
        var authorize = Query(start.AuthorizationUrl);
        authorize["state"].Should().Be(start.State);
        authorize["code_challenge_method"].Should().Be("S256");
        authorize["redirect_uri"].Should().EndWith("/auth/google");
        app.OAuth.RegisterGoogleToken(
            "google-link-code",
            new OAuthUser("google-linked-subject", "someone.else@gmail.com", "Linked Person", "google", true),
            nonce: authorize["nonce"]);

        var complete = await CompleteAsync(app, session, "google", new CompleteProviderLinkRequest
        {
            State = start.State,
            Code = "google-link-code",
        });

        complete.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(complete));
        var link = (await app.ReadApiResponseAsync<ProviderLinkResponse>(complete)).Data!;
        link.Provider.Should().Be("google");
        link.Email.Should().Be("someone.else@gmail.com");
        var exchange = app.OAuth.GoogleExchanges.Should().ContainSingle().Subject;
        exchange.RedirectUri.Should().Be(authorize["redirect_uri"]);
        Challenge(exchange.CodeVerifier).Should().Be(authorize["code_challenge"]);
        (await app.FindLinkedSubjectAsync(Email("links-google"), IdentityProviders.Google))
            .Should().Be("google-linked-subject");

        var listed = (await app.ReadApiResponseAsync<ProviderLinksResponse>(
            await app.GetWithBearerAsync("/api/auth/providers", session.AccessToken))).Data!;
        var google = listed.Providers.Single(p => p.Provider == "google");
        google.Link!.Email.Should().Be("someone.else@gmail.com");
        google.CanUnlink.Should().BeTrue();

        // The state is spent.
        var replay = await CompleteAsync(app, session, "google", new CompleteProviderLinkRequest
        {
            State = start.State,
            Code = "google-link-code",
        });
        await AssertErrorAsync(app, replay, HttpStatusCode.Gone, ProviderLinkExpiredException.Code);
    }

    [Fact]
    public async Task MicrosoftLink_ShouldRefuseATokenWithoutTheServersNonce()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-ms");
        await app.GrantStepUpProofAsync(session.AccessToken);
        var identity = new OAuthUser("ms-linked-subject", "person@outlook.com", "Person", "microsoft", true);

        var first = await StartAsync(app, session, "microsoft");
        first.CodeVerifier.Should().NotBeNullOrWhiteSpace("the browser redeems Microsoft's code");
        app.OAuth.RegisterMicrosoftToken("ms-token-wrong-nonce", identity, nonce: "some-other-nonce");

        var refused = await CompleteAsync(app, session, "microsoft", new CompleteProviderLinkRequest
        {
            State = first.State,
            IdToken = "ms-token-wrong-nonce",
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await app.FindLinkedSubjectAsync(Email("links-ms"), IdentityProviders.Microsoft)).Should().BeNull();

        var second = await StartAsync(app, session, "microsoft");
        app.OAuth.RegisterMicrosoftToken("ms-token", identity, nonce: Query(second.AuthorizationUrl)["nonce"]);

        var linked = await CompleteAsync(app, session, "microsoft", new CompleteProviderLinkRequest
        {
            State = second.State,
            IdToken = "ms-token",
        });

        linked.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(linked));
        (await app.FindLinkedSubjectAsync(Email("links-ms"), IdentityProviders.Microsoft))
            .Should().Be("ms-linked-subject");
    }

    /// <summary>
    /// A state is bound to the account and session that started it. Presenting it from another
    /// account links nothing, and spends it, so it cannot be replayed from the right one.
    /// </summary>
    [Fact]
    public async Task Complete_ShouldRefuseAStateStartedByAnotherAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await SignUpAsync(app, "links-owner");
        var other = await SignUpAsync(app, "links-other");
        await app.GrantStepUpProofAsync(owner.AccessToken);
        await app.GrantStepUpProofAsync(other.AccessToken);
        var start = await StartAsync(app, owner, "google");
        app.OAuth.RegisterGoogleToken(
            "google-stolen-code",
            new OAuthUser("google-stolen", "x@gmail.com", "X", "google", true),
            nonce: Query(start.AuthorizationUrl)["nonce"]);
        var request = new CompleteProviderLinkRequest { State = start.State, Code = "google-stolen-code" };

        var stolen = await CompleteAsync(app, other, "google", request);

        await AssertErrorAsync(app, stolen, HttpStatusCode.Forbidden, ProviderLinkMismatchException.Code);
        (await app.FindLinkedSubjectAsync(Email("links-other"), IdentityProviders.Google)).Should().BeNull();

        var afterwards = await CompleteAsync(app, owner, "google", request);
        await AssertErrorAsync(app, afterwards, HttpStatusCode.Gone, ProviderLinkExpiredException.Code);
    }

    [Fact]
    public async Task ProviderEndpoints_ShouldRequireAFreshStepUp()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-gate");

        var start = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/providers/google/link/start", new { }, session.AccessToken);
        await AssertErrorAsync(app, start, HttpStatusCode.Forbidden, "MFA_REQUIRED");

        var unlink = await UnlinkAsync(app, session, "google");
        await AssertErrorAsync(app, unlink, HttpStatusCode.Forbidden, "MFA_REQUIRED");

        // Reading the list needs no proof.
        (await app.GetWithBearerAsync("/api/auth/providers", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A provider subject maps to at most one account, whichever account asks.</summary>
    [Fact]
    public async Task Complete_ShouldRefuseAnIdentityAlreadyLinkedToAnotherAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        await app.SeedUserAsync("holder@example.com", googleId: "google-held");
        var session = await SignUpAsync(app, "links-dup");
        await app.GrantStepUpProofAsync(session.AccessToken);
        var start = await StartAsync(app, session, "google");
        app.OAuth.RegisterGoogleToken(
            "google-held-code",
            new OAuthUser("google-held", Email("links-dup"), "Dup", "google", true),
            nonce: Query(start.AuthorizationUrl)["nonce"]);

        var response = await CompleteAsync(app, session, "google", new CompleteProviderLinkRequest
        {
            State = start.State,
            Code = "google-held-code",
        });

        await AssertErrorAsync(app, response, HttpStatusCode.Conflict, ProviderLinkConflictException.AlreadyLinkedCode);
        (await app.FindLinkedSubjectAsync("holder@example.com", IdentityProviders.Google)).Should().Be("google-held");
        (await app.FindLinkedSubjectAsync(Email("links-dup"), IdentityProviders.Google)).Should().BeNull();
    }

    /// <summary>
    /// Two accounts racing to link one identity: the unique index decides, and the loser is told
    /// the identity is taken rather than seeing a server error.
    /// </summary>
    [Fact]
    public async Task ConcurrentLinksOfOneIdentity_ShouldLeaveItLinkedToExactlyOneAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var first = await SignUpAsync(app, "race-first");
        var second = await SignUpAsync(app, "race-second");
        await app.GrantStepUpProofAsync(first.AccessToken);
        await app.GrantStepUpProofAsync(second.AccessToken);
        var identity = new OAuthUser("google-contested", "contested@gmail.com", "Contested", "google", true);
        var firstStart = await StartAsync(app, first, "google");
        var secondStart = await StartAsync(app, second, "google");
        app.OAuth.RegisterGoogleToken("race-code-1", identity, nonce: Query(firstStart.AuthorizationUrl)["nonce"]);
        app.OAuth.RegisterGoogleToken("race-code-2", identity, nonce: Query(secondStart.AuthorizationUrl)["nonce"]);

        var responses = await Task.WhenAll(
            CompleteAsync(app, first, "google", new CompleteProviderLinkRequest { State = firstStart.State, Code = "race-code-1" }),
            CompleteAsync(app, second, "google", new CompleteProviderLinkRequest { State = secondStart.State, Code = "race-code-2" }));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await app.QueryDbAsync(db => db.UserIdentityLinks.CountAsync(l => l.Subject == "google-contested")))
            .Should().Be(1);
    }

    [Fact]
    public async Task Unlink_ShouldKeepTheLastSignInMethod_AndRemoveAnyOther()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, userId) = await SignUpWithGoogleAsync(app, "solo-google", "google-solo");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var overview = (await app.ReadApiResponseAsync<ProviderLinksResponse>(
            await app.GetWithBearerAsync("/api/auth/providers", session.AccessToken))).Data!;
        overview.HasPassword.Should().BeFalse();
        overview.Providers.Single(p => p.Provider == "google").CanUnlink.Should().BeFalse();

        var refused = await UnlinkAsync(app, session, "google");
        await AssertErrorAsync(app, refused, HttpStatusCode.Conflict, ProviderLinkConflictException.LastSignInMethodCode);

        var start = await StartAsync(app, session, "microsoft");
        app.OAuth.RegisterMicrosoftToken(
            "solo-ms-token",
            new OAuthUser("ms-solo", "solo@outlook.com", "Solo", "microsoft", true),
            nonce: Query(start.AuthorizationUrl)["nonce"]);
        (await CompleteAsync(app, session, "microsoft", new CompleteProviderLinkRequest
        {
            State = start.State,
            IdToken = "solo-ms-token",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var versionBefore = await AuthVersionAsync(app, userId);
        var removed = await UnlinkAsync(app, session, "google");

        removed.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(removed));
        (await app.FindLinkedSubjectAsync(Email("solo-google"), IdentityProviders.Google)).Should().BeNull();
        (await app.FindLinkedSubjectAsync(Email("solo-google"), IdentityProviders.Microsoft)).Should().Be("ms-solo");

        // Nothing issued before the removal keeps access, the caller's own session included.
        (await AuthVersionAsync(app, userId)).Should().Be(versionBefore + 1);
        (await app.GetWithBearerAsync("/api/auth/providers", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var history = await HistoryKindsAsync(app, userId);
        history.Should().Contain(AccountSecurityEventKind.ProviderLinked);
        history.Should().Contain(AccountSecurityEventKind.ProviderUnlinked);
        history.Should().Contain(AccountSecurityEventKind.SessionsRevoked);
    }

    private static Task<int> AuthVersionAsync(AuthApiTestApp app, int userId) =>
        app.QueryDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.AuthVersion).SingleAsync());

    /// <summary>
    /// Two removals of a password-less account's last two links, at once: the account row lock
    /// serializes them, so one succeeds and the other finds it would remove the last method.
    /// </summary>
    [Fact]
    public async Task ConcurrentUnlinks_ShouldNeverRemoveTheLastSignInMethod()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var (session, _) = await SignUpWithGoogleAsync(app, "both-links", "google-both");
        await app.GrantStepUpProofAsync(session.AccessToken);
        var start = await StartAsync(app, session, "microsoft");
        app.OAuth.RegisterMicrosoftToken(
            "both-ms-token",
            new OAuthUser("ms-both", "both@outlook.com", "Both", "microsoft", true),
            nonce: Query(start.AuthorizationUrl)["nonce"]);
        (await CompleteAsync(app, session, "microsoft", new CompleteProviderLinkRequest
        {
            State = start.State,
            IdToken = "both-ms-token",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var responses = await Task.WhenAll(
            UnlinkAsync(app, session, "google"),
            UnlinkAsync(app, session, "microsoft"));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await app.QueryDbAsync(db => db.UserIdentityLinks.CountAsync(l => l.Email != null && l.Subject.EndsWith("-both"))))
            .Should().Be(1);
    }

    /// <summary>A change to the account's links revokes every link flow it still had outstanding.</summary>
    [Fact]
    public async Task LinkChange_ShouldRevokeOutstandingLinkStates()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await SignUpAsync(app, "links-revoke");
        await app.GrantStepUpProofAsync(session.AccessToken);
        var stale = await StartAsync(app, session, "microsoft");
        var current = await StartAsync(app, session, "google");
        app.OAuth.RegisterGoogleToken(
            "revoke-code",
            new OAuthUser("google-revoke", "r@gmail.com", "R", "google", true),
            nonce: Query(current.AuthorizationUrl)["nonce"]);
        app.OAuth.RegisterMicrosoftToken(
            "revoke-ms-token",
            new OAuthUser("ms-revoke", "r@outlook.com", "R", "microsoft", true),
            nonce: Query(stale.AuthorizationUrl)["nonce"]);

        (await CompleteAsync(app, session, "google", new CompleteProviderLinkRequest
        {
            State = current.State,
            Code = "revoke-code",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var afterChange = await CompleteAsync(app, session, "microsoft", new CompleteProviderLinkRequest
        {
            State = stale.State,
            IdToken = "revoke-ms-token",
        });
        await AssertErrorAsync(app, afterChange, HttpStatusCode.Gone, ProviderLinkExpiredException.Code);
    }

    private static string Email(string name) => $"{name}@example.com";

    private static Task<AuthenticatedSessionResponse> SignUpAsync(AuthApiTestApp app, string name) =>
        app.SignUpAndVerifyByTokenAsync(Email(name), transport: SessionTransportResolver.ApiValue);

    /// <summary>A password-less account whose only sign-in method is a Google link.</summary>
    private static async Task<(AuthenticatedSessionResponse Session, int UserId)> SignUpWithGoogleAsync(
        AuthApiTestApp app,
        string name,
        string subject)
    {
        var token = $"{subject}-token";
        app.OAuth.RegisterGoogleToken(token, new OAuthUser(subject, Email(name), name, "google", true));
        var pending = await app.PostJsonWithCsrfAsync("/api/auth/google", new GoogleRequest
        {
            Token = token,
            Transport = SessionTransportResolver.ApiValue,
        });
        var signupToken = (await app.ReadApiResponseAsync<OAuthAuthenticationResponse>(pending)).Data!.SignupToken!;
        var complete = await app.PostJsonWithCsrfAsync("/api/auth/oauth/complete", new CompleteOAuthSignupRequest
        {
            SignupToken = signupToken,
            Usertype = "Participant",
            Username = name,
            Transport = SessionTransportResolver.ApiValue,
        });
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(complete));
        var session = (await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(complete)).Data!;
        var user = await app.FindUserByEmailAsync(Email(name));
        user!.Password.Should().BeNull();
        return (session, user.Id);
    }

    private static async Task<ProviderLinkStartResponse> StartAsync(
        AuthApiTestApp app,
        AuthenticatedSessionResponse session,
        string provider)
    {
        var response = await app.PostJsonWithBearerAndCsrfAsync(
            $"/api/auth/providers/{provider}/link/start",
            new { },
            session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<ProviderLinkStartResponse>(response)).Data!;
    }

    private static Task<HttpResponseMessage> CompleteAsync(
        AuthApiTestApp app,
        AuthenticatedSessionResponse session,
        string provider,
        CompleteProviderLinkRequest request) =>
        app.PostJsonWithBearerAndCsrfAsync($"/api/auth/providers/{provider}/link/complete", request, session.AccessToken);

    private static async Task<HttpResponseMessage> UnlinkAsync(
        AuthApiTestApp app,
        AuthenticatedSessionResponse session,
        string provider)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/providers/{provider}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Add(backend.main.application.security.CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(
        AuthApiTestApp app,
        HttpResponseMessage response,
        HttpStatusCode status,
        string? code)
    {
        response.StatusCode.Should().Be(status, await app.DescribeFailureAsync(response));
        if (code != null)
            (await response.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    private static async Task<IReadOnlyList<AccountSecurityEventKind>> HistoryKindsAsync(AuthApiTestApp app, int userId)
    {
        await app.Services.GetRequiredService<SecurityHistoryOutboxDrainer>().DrainAllAsync();
        var aggregateId = userId.ToString();
        var rows = await app.QueryDbAsync(db => db.SecurityHistoryOutbox
            .AsNoTracking()
            .Where(row => row.AggregateId == aggregateId)
            .ToListAsync());
        return rows.Select(row => SecurityHistoryMessageParser.Parse(row.Payload).Kind).ToList();
    }

    private static Dictionary<string, string> Query(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

    private static string Challenge(string verifier) =>
        WebEncoders.Base64UrlEncode(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
}
