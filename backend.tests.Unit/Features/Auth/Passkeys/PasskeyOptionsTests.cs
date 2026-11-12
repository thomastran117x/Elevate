using backend.main.features.auth.passkeys;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.Passkeys;

public class PasskeyOptionsTests
{
    private static readonly PasskeyOptionsValidator Production = new(isNonProduction: false);
    private static readonly PasskeyOptionsValidator Development = new(isNonProduction: true);

    [Fact]
    public void ApplyFrontendDefaults_ShouldTakeTheRpIdAndOriginFromTheFrontendAddress()
    {
        var options = new PasskeyOptions();

        options.ApplyFrontendDefaults("https://app.example.com/some/path");

        options.RelyingPartyId.Should().Be("app.example.com");
        options.Origins.Should().Equal("https://app.example.com");
    }

    [Fact]
    public void ApplyFrontendDefaults_ShouldKeepExplicitConfiguration()
    {
        var options = new PasskeyOptions { RelyingPartyId = "example.com", Origins = ["https://www.example.com"] };

        options.ApplyFrontendDefaults("https://other.example.org");

        options.RelyingPartyId.Should().Be("example.com");
        options.Origins.Should().Equal("https://www.example.com");
    }

    [Fact]
    public void ApplyFrontendDefaults_ShouldTreatBlankEntriesAsUnset()
    {
        var options = new PasskeyOptions { RelyingPartyId = "", Origins = [""] };

        options.ApplyFrontendDefaults("http://localhost:3090");

        options.RelyingPartyId.Should().Be("localhost");
        options.Origins.Should().Equal("http://localhost:3090");
    }

    [Fact]
    public void ApplyFrontendDefaults_ShouldIgnoreAnAddressThatIsNotAbsolute()
    {
        var options = new PasskeyOptions();

        options.ApplyFrontendDefaults("not a url");

        options.RelyingPartyId.Should().BeNull();
        options.Origins.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldAcceptARegistrableDomainAndItsSubdomainOrigins()
    {
        var result = Production.Validate(null, new PasskeyOptions
        {
            RelyingPartyId = "example.com",
            Origins = ["https://example.com", "https://app.example.com:8443"],
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldAcceptLocalhostOverHttp_OnlyOutsideProduction()
    {
        var options = new PasskeyOptions { RelyingPartyId = "localhost", Origins = ["http://localhost:3090"] };

        Development.Validate(null, options).Succeeded.Should().BeTrue();
        Production.Validate(null, options).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Example.com")]
    [InlineData("https://example.com")]
    [InlineData("example.com:443")]
    [InlineData("127.0.0.1")]
    [InlineData("com")]
    [InlineData(".example.com")]
    public void Validate_ShouldRefuseAnRpIdThatIsNotABareRegistrableDomain(string? rpId)
    {
        var result = Production.Validate(null, new PasskeyOptions
        {
            RelyingPartyId = rpId,
            Origins = ["https://example.com"],
        });

        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://evil.example")]
    [InlineData("https://notexample.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://user@example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    public void Validate_ShouldRefuseAnOriginOutsideTheRpIdOrNotBareHttps(string origin)
    {
        var result = Production.Validate(null, new PasskeyOptions
        {
            RelyingPartyId = "example.com",
            Origins = [origin],
        });

        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldRefuseNoOriginsAndOutOfRangeLimits()
    {
        var result = Production.Validate(null, new PasskeyOptions
        {
            RelyingPartyId = "example.com",
            RelyingPartyName = " ",
            Origins = [],
            CeremonyTimeoutSeconds = 5,
            MaxCredentialsPerUser = 0,
        });

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(4);
    }

    [Fact]
    public void CeremonyTimeout_ShouldFollowTheConfiguredSeconds()
    {
        new PasskeyOptions { CeremonyTimeoutSeconds = 120 }.CeremonyTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }
}
