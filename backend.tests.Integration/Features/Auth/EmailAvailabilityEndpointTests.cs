using System.Net;

using backend.main.features.auth.contracts.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// Exercises the email availability endpoint against a real Postgres and Redis, so the two-tier
/// bloom filter path runs for real rather than against a stub.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class EmailAvailabilityEndpointTests
{
    [Fact]
    public async Task CheckAvailability_ShouldReportAnUnusedAddressAsAvailable()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.GetAsync(
            "/api/auth/email/availability?email=never-registered@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await app.ReadApiResponseAsync<EmailAvailabilityResponse>(response);
        body.Data!.Available.Should().BeTrue();
        body.Data.Email.Should().Be("never-registered@example.com");
    }

    /// <summary>
    /// Seeded users are written straight through the DbContext and never touch the filter, so a
    /// correct answer here proves the endpoint still confirms against the database.
    /// </summary>
    [Fact]
    public async Task CheckAvailability_ShouldReportASeededAddressAsRegistered()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        await app.SeedUserAsync("seeded-email@example.com", username: "seeded-email-user");

        var response = await app.Client.GetAsync(
            "/api/auth/email/availability?email=seeded-email@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await app.ReadApiResponseAsync<EmailAvailabilityResponse>(response);
        body.Data!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAvailability_ShouldReflectAnAddressClaimedThroughSignup()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var before = await app.Client.GetAsync(
            "/api/auth/email/availability?email=claimed-email@example.com");
        (await app.ReadApiResponseAsync<EmailAvailabilityResponse>(before)).Data!.Available
            .Should().BeTrue();

        await app.SignUpAndVerifyByTokenAsync("claimed-email@example.com", username: "claimed-email-user");

        var after = await app.Client.GetAsync(
            "/api/auth/email/availability?email=claimed-email@example.com");
        (await app.ReadApiResponseAsync<EmailAvailabilityResponse>(after)).Data!.Available
            .Should().BeFalse();
    }

    /// <summary>
    /// The column is citext so the database would match either way, but the filter hashes the
    /// literal string — this is what proves the probe and the source agree on one spelling.
    /// </summary>
    [Fact]
    public async Task CheckAvailability_ShouldNormaliseBeforeAnswering()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        await app.SeedUserAsync("mixed-case-email@example.com", username: "mixed-case-email-user");

        var response = await app.Client.GetAsync(
            "/api/auth/email/availability?email=%20Mixed-Case-Email%40Example.COM%20");

        var body = await app.ReadApiResponseAsync<EmailAvailabilityResponse>(response);
        body.Data!.Email.Should().Be("mixed-case-email@example.com");
        body.Data.Available.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("@example.com")]
    public async Task CheckAvailability_ShouldRejectAddressesThePolicyDisallows(string email)
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.GetAsync($"/api/auth/email/availability?email={email}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CheckAvailability_ShouldRejectAnOverLengthAddress()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var tooLong = new string('a', 250) + "@example.com";

        var response = await app.Client.GetAsync($"/api/auth/email/availability?email={tooLong}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CheckAvailability_ShouldNotRequireAuthentication()
    {
        // It serves the signup form, where there is no session yet.
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.GetAsync(
            "/api/auth/email/availability?email=anonymous-probe@example.com");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The two filters share one registry, and the target name is mixed into the hash — so an
    /// address must never make its own local part look taken as a username, or vice versa.
    /// </summary>
    [Fact]
    public async Task CheckAvailability_ShouldNotLeakBetweenTheUsernameAndEmailFilters()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        await app.SignUpAndVerifyByTokenAsync("separation@example.com", username: "separation-user");

        var email = await app.Client.GetAsync(
            "/api/auth/email/availability?email=separation-user@example.com");
        (await app.ReadApiResponseAsync<EmailAvailabilityResponse>(email)).Data!.Available
            .Should().BeTrue();

        var username = await app.Client.GetAsync(
            "/api/auth/username/availability?username=separation@example.com");
        (await app.ReadApiResponseAsync<UsernameAvailabilityResponse>(username)).Data!.Available
            .Should().BeTrue();
    }
}
