using System.Net;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.auth.token;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Recovery-code generation, status, and storage against PostgreSQL.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RecoveryCodeEndpointsTests
{
    private const string StatusPath = "/api/auth/mfa/recovery-codes/status";
    private const string RegeneratePath = "/api/auth/mfa/recovery-codes/regenerate";

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

    internal static async Task<IReadOnlyList<string>> GenerateAsync(AuthApiTestApp app, string accessToken)
    {
        await app.GrantStepUpProofAsync(accessToken);
        var response = await PostRegenerateAsync(app, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<RecoveryCodesResponse>(response)).Data!.Codes;
    }

    internal static async Task<RecoveryCodeStatusResponse> ReadStatusAsync(AuthApiTestApp app, string accessToken)
    {
        var response = await app.GetWithBearerAsync(StatusPath, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<RecoveryCodeStatusResponse>(response)).Data!;
    }

    private static Task<HttpResponseMessage> PostRegenerateAsync(AuthApiTestApp app, string accessToken) =>
        app.SendWithBearerAndCsrfAsync(HttpMethod.Post, RegeneratePath, null, accessToken);

    private static string Selector(RecoveryCodeHasher hasher, int userId, string code)
    {
        RecoveryCodeFormat.TryNormalize(code, out var normalized).Should().BeTrue();
        return hasher.Selector(userId, normalized);
    }
}
