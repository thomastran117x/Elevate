using System.Net;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.recovery;
using backend.main.infrastructure.database.core;
using backend.main.features.auth.token;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OtpNet;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Recovery-code generation, storage, and single-use consumption against PostgreSQL.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RecoveryCodeEndpointsTests
{
    [Fact]
    public async Task Status_ShouldReportNoCodes_BeforeAnyAreGenerated()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-empty@example.com", transport: SessionTransportResolver.ApiValue);

        var status = await ReadStatusAsync(app, session.AccessToken);

        status.RemainingCount.Should().Be(0);
        status.TotalCount.Should().Be(10);
        status.GeneratedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Regenerate_ShouldRequireAFreshStepUp_ThenReturnTenCodesStoredOnlyAsHashes()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-generate@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-generate@example.com");

        var gated = await PostRegenerateAsync(app, session.AccessToken);
        gated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gated.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");

        await app.GrantStepUpProofAsync(session.AccessToken);
        await app.MarkNotificationBoundaryAsync();
        var response = await PostRegenerateAsync(app, session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var codes = (await app.ReadApiResponseAsync<RecoveryCodesResponse>(response)).Data!.Codes;
        codes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(code => code.Should().MatchRegex("^([0-9A-HJKMNP-TV-Z]{4}-){6}[0-9A-HJKMNP-TV-Z]{4}$"));

        var rows = await app.QueryDbAsync(db => db.RecoveryCodes.AsNoTracking().Where(c => c.UserId == user!.Id).ToListAsync());
        rows.Should().HaveCount(10).And.OnlyContain(row => row.UsedAtUtc == null);
        rows.Should().OnlyContain(row => row.CodeHash.StartsWith("$argon2id$"));
        foreach (var code in codes)
        {
            RecoveryCodeFormat.TryNormalize(code, out var normalized).Should().BeTrue();
            rows.Should().NotContain(row => row.CodeHash.Contains(normalized) || row.Selector.Contains(normalized));
        }

        var status = await ReadStatusAsync(app, session.AccessToken);
        status.RemainingCount.Should().Be(10);
        status.GeneratedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Generation spends the proof, like any other factor change.
        (await PostRegenerateAsync(app, session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var notice = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.MfaFactorChanged && m.Email == "rc-generate@example.com");
        notice.MfaFactor.Should().Be("recovery_codes");
        notice.MfaChange.Should().Be("enabled");
        notice.Code.Should().BeNull();
        notice.Token.Should().BeNull();
    }

    [Fact]
    public async Task Regenerate_ShouldReplaceTheWholeSet()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-replace@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-replace@example.com");

        var first = await GenerateAsync(app, session.AccessToken);
        await app.MarkNotificationBoundaryAsync();
        var second = await GenerateAsync(app, session.AccessToken);

        second.Should().NotIntersectWith(first);
        var hasher = app.Services.GetRequiredService<RecoveryCodeHasher>();
        var selectors = await app.QueryDbAsync(db => db.RecoveryCodes.AsNoTracking()
            .Where(c => c.UserId == user!.Id)
            .Select(c => c.Selector)
            .ToListAsync());
        selectors.Should().BeEquivalentTo(second.Select(code => Selector(hasher, user!.Id, code)));

        var notice = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.MfaFactorChanged && m.Email == "rc-replace@example.com");
        notice.MfaChange.Should().Be("regenerated");
    }

    [Fact]
    public async Task ConcurrentRegenerations_ShouldLeaveExactlyOneSet()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-race@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-race@example.com");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostRegenerateAsync(app, session.AccessToken)));

        // A request that reaches the gate after another has spent the proof is refused; every
        // other one replaces the set, one after another, under the account lock.
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Forbidden);
        var returned = new List<IReadOnlyList<string>>();
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.OK))
            returned.Add((await app.ReadApiResponseAsync<RecoveryCodesResponse>(response)).Data!.Codes);
        returned.Should().NotBeEmpty();

        var hasher = app.Services.GetRequiredService<RecoveryCodeHasher>();
        var selectors = await app.QueryDbAsync(db => db.RecoveryCodes.AsNoTracking()
            .Where(c => c.UserId == user!.Id)
            .Select(c => c.Selector)
            .ToListAsync());
        selectors.Should().HaveCount(10);
        returned.Should().ContainSingle(set =>
            set.Select(code => Selector(hasher, user!.Id, code)).ToHashSet().SetEquals(selectors));
    }

    [Fact]
    public async Task ReplacingTheSet_ShouldRetireEarlierProofs_EvenWithoutTheCacheGenerationBump()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-proof@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-proof@example.com");
        await GenerateAsync(app, session.AccessToken);
        await app.GrantStepUpProofAsync(session.AccessToken);
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Replace the set the way a regeneration commits it, but skip the factor-change follow-up
        // that advances the Redis generation, as if Redis had been unreachable at that moment.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<RecoveryCodeHasher>();
            var repository = new RecoveryCodeRepository(scope.ServiceProvider.GetRequiredService<AppDatabaseContext>());
            var generatedAt = DateTime.UtcNow;
            var rows = RecoveryCodeFormat.GenerateSet().Select(code => new RecoveryCode
            {
                Selector = hasher.Selector(user!.Id, code),
                CodeHash = hasher.Hash(user.Id, code),
                CreatedAtUtc = generatedAt,
            }).ToList();
            (await repository.ReplaceSetAsync(user!.Id, rows)).Should().BeTrue();
        }

        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Codes_ShouldBeDeletedWithTheAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-delete@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-delete@example.com");
        await GenerateAsync(app, session.AccessToken);

        await app.GrantStepUpProofAsync(session.AccessToken);
        var deleted = await app.SendWithBearerAndCsrfAsync(HttpMethod.Delete, "/api/profile", null, session.AccessToken);
        deleted.IsSuccessStatusCode.Should().BeTrue(await app.DescribeFailureAsync(deleted));

        (await app.QueryDbAsync(db => db.RecoveryCodes.CountAsync(c => c.UserId == user!.Id))).Should().Be(0);
    }

    [Fact]
    public async Task SessionStepUp_ShouldAcceptACodeOnce_AndCountItDown()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-stepup@example.com", transport: SessionTransportResolver.ApiValue);
        var codes = await GenerateAsync(app, session.AccessToken);

        var options = await app.GetWithBearerAsync("/api/auth/mfa/step-up/options", session.AccessToken);
        (await app.ReadApiResponseAsync<SessionMfaOptionsResponse>(options)).Data!.AvailableMethods
            .Should().Equal("recovery_code", "email");

        // Lowercase and without separators: entry is forgiving.
        var first = await VerifyStepUpAsync(app, session.AccessToken, codes[0].Replace("-", string.Empty).ToLowerInvariant());
        first.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(first));
        (await app.ReadStepUpProofAsync(session.AccessToken))!.Method.Should().Be("recovery_code");
        (await app.GetWithBearerAsync("/api/auth/mfa/step-up/status", session.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStatusAsync(app, session.AccessToken)).RemainingCount.Should().Be(9);

        (await VerifyStepUpAsync(app, session.AccessToken, codes[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await VerifyStepUpAsync(app, session.AccessToken, RecoveryCodeFormat.Display(RecoveryCodeFormat.GenerateSet()[0])))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await VerifyStepUpAsync(app, session.AccessToken, "123456")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadStatusAsync(app, session.AccessToken)).RemainingCount.Should().Be(9);
    }

    [Fact]
    public async Task SessionStepUp_ShouldRefuseAnotherAccountsCode_AndCodesFromAReplacedSet()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await app.SignUpAndVerifyByTokenAsync("rc-owner@example.com", transport: SessionTransportResolver.ApiValue);
        var other = await app.SignUpAndVerifyByTokenAsync("rc-other@example.com", transport: SessionTransportResolver.ApiValue);
        var ownerCodes = await GenerateAsync(app, owner.AccessToken);
        await GenerateAsync(app, other.AccessToken);

        (await VerifyStepUpAsync(app, other.AccessToken, ownerCodes[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await GenerateAsync(app, owner.AccessToken);
        (await VerifyStepUpAsync(app, owner.AccessToken, ownerCodes[1])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadStatusAsync(app, owner.AccessToken)).RemainingCount.Should().Be(10);
    }

    [Fact]
    public async Task ConcurrentStepUps_WithOneCode_ShouldSucceedExactlyOnce()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-concurrent@example.com", transport: SessionTransportResolver.ApiValue);
        var codes = await GenerateAsync(app, session.AccessToken);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => VerifyStepUpAsync(app, session.AccessToken, codes[0])));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.TooManyRequests);
        (await ReadStatusAsync(app, session.AccessToken)).RemainingCount.Should().Be(9);
    }

    [Fact]
    public async Task ConcurrentConsumption_InTheDatabase_ShouldSpendACodeExactlyOnce()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-db-race@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-db-race@example.com");
        await GenerateAsync(app, session.AccessToken);
        var codeId = await app.QueryDbAsync(db => db.RecoveryCodes.Where(c => c.UserId == user!.Id).Select(c => c.Id).FirstAsync());

        // Each attempt gets its own scope, so its own DbContext and connection, like separate requests.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = app.Services.CreateAsyncScope();
            var repository = new RecoveryCodeRepository(scope.ServiceProvider.GetRequiredService<AppDatabaseContext>());
            return await repository.TryConsumeAsync(codeId, user!.Id, DateTime.UtcNow);
        }));

        results.Count(spent => spent).Should().Be(1);
        (await app.QueryDbAsync(db => db.RecoveryCodes.CountAsync(c => c.Id == codeId && c.UsedAtUtc != null))).Should().Be(1);
    }

    [Fact]
    public async Task LoginStepUp_ShouldAcceptARecoveryCode_WithoutTrustingTheDevice()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-login@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-login@example.com");
        await EnrollTotpAsync(app, session.AccessToken);
        var codes = await GenerateAsync(app, session.AccessToken);
        var devicesBefore = await app.QueryDbAsync(db => db.Devices.CountAsync(d => d.UserId == user!.Id));

        var challenge = await StartLoginAsync(app, "rc-login");
        challenge.AvailableMethods.Should().Equal("totp", "recovery_code", "email");

        var start = await app.PostJsonWithCsrfAsync("/api/auth/mfa/start", new StartLoginStepUpRequest
        {
            Challenge = challenge.Challenge,
            Method = "recovery_code",
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(start));

        var wrong = await VerifyLoginAsync(app, challenge.Challenge, RecoveryCodeFormat.Display(RecoveryCodeFormat.GenerateSet()[0]));
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var verified = await VerifyLoginAsync(app, challenge.Challenge, codes[3]);
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(verified));
        var signedIn = (await app.ReadApiResponseAsync<AuthenticatedSessionResponse>(verified)).Data!;
        signedIn.AccessToken.Should().NotBeNullOrWhiteSpace();

        (await app.QueryDbAsync(db => db.Devices.CountAsync(d => d.UserId == user!.Id))).Should().Be(devicesBefore);
        (await ReadStatusAsync(app, signedIn.AccessToken)).RemainingCount.Should().Be(9);

        // The spent code cannot complete another sign-in.
        var again = await StartLoginAsync(app, "rc-login");
        (await VerifyLoginAsync(app, again.Challenge, codes[3])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginStepUp_ShouldNotSpendACode_ForADisabledAccount()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-disabled@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-disabled@example.com");
        await EnrollTotpAsync(app, session.AccessToken);
        var codes = await GenerateAsync(app, session.AccessToken);
        var challenge = await StartLoginAsync(app, "rc-disabled");

        await app.QueryDbAsync(db => db.Users
            .Where(u => u.Id == user!.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsDisabled, true)));

        (await VerifyLoginAsync(app, challenge.Challenge, codes[0])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await app.QueryDbAsync(db => db.RecoveryCodes.CountAsync(c => c.UserId == user!.Id && c.UsedAtUtc != null)))
            .Should().Be(0);
    }

    [Fact]
    public async Task LoginStepUp_ShouldNotSpendACode_ForAnExpiredChallenge()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync("rc-expired@example.com", transport: SessionTransportResolver.ApiValue);
        var user = await app.FindUserByEmailAsync("rc-expired@example.com");
        await EnrollTotpAsync(app, session.AccessToken);
        var codes = await GenerateAsync(app, session.AccessToken);

        (await VerifyLoginAsync(app, "expired-or-unknown-challenge", codes[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.QueryDbAsync(db => db.RecoveryCodes.CountAsync(c => c.UserId == user!.Id && c.UsedAtUtc != null)))
            .Should().Be(0);
    }

    internal static async Task<IReadOnlyList<string>> GenerateAsync(AuthApiTestApp app, string accessToken)
    {
        await app.GrantStepUpProofAsync(accessToken);
        var response = await PostRegenerateAsync(app, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<RecoveryCodesResponse>(response)).Data!.Codes;
    }

    internal static async Task<RecoveryCodeStatusResponse> ReadStatusAsync(AuthApiTestApp app, string accessToken)
    {
        var response = await app.GetWithBearerAsync("/api/auth/mfa/recovery-codes/status", accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<RecoveryCodeStatusResponse>(response)).Data!;
    }

    private static Task<HttpResponseMessage> VerifyStepUpAsync(AuthApiTestApp app, string accessToken, string code) =>
        app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/verify",
            new SessionMfaVerifyRequest { Method = "recovery_code", Code = code },
            accessToken);

    private static Task<HttpResponseMessage> VerifyLoginAsync(AuthApiTestApp app, string challenge, string code) =>
        app.PostJsonWithCsrfAsync("/api/auth/mfa/verify/recovery-code", new VerifyLoginStepUpRequest
        {
            Challenge = challenge,
            Code = code,
        });

    private static async Task<LoginStepUpChallengeResponse> StartLoginAsync(AuthApiTestApp app, string username)
    {
        var login = await app.PostJsonWithCsrfAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = AuthApiTestApp.DefaultPassword,
            Captcha = "captcha",
            Transport = SessionTransportResolver.ApiValue,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(login));
        var body = (await app.ReadApiResponseAsync<LoginAuthenticationResponse>(login)).Data!;
        body.Type.Should().Be("requires_step_up");
        return body.StepUp!;
    }

    /// <summary>Turns on TOTP, which is what makes an unknown device face a step-up challenge.</summary>
    private static async Task EnrollTotpAsync(AuthApiTestApp app, string accessToken)
    {
        await app.GrantStepUpProofAsync(accessToken);
        var start = await app.PostJsonWithBearerAndCsrfAsync("/api/auth/mfa/totp/enroll/start", new { }, accessToken);
        var secret = (await app.ReadApiResponseAsync<TotpEnrollmentStartResponse>(start)).Data!.SecretKey;
        var verify = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/totp/enroll/verify",
            new TotpEnrollmentVerifyRequest { Code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp() },
            accessToken);
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(verify));
    }

    private static Task<HttpResponseMessage> PostRegenerateAsync(AuthApiTestApp app, string accessToken) =>
        app.PostJsonWithBearerAndCsrfAsync("/api/auth/mfa/recovery-codes/regenerate", new { }, accessToken);

    private static string Selector(RecoveryCodeHasher hasher, int userId, string code)
    {
        RecoveryCodeFormat.TryNormalize(code, out var normalized).Should().BeTrue();
        return hasher.Selector(userId, normalized);
    }
}
