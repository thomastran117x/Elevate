using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using backend.main.features.auth.events;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.Events;

public sealed class AuthSecurityEventContractTests
{
    [Fact]
    public void Catalog_ShouldKeepEveryPublishedIdAndName()
    {
        // Dashboards, alerts, and the persisted history key on these pairs. Adding an event appends
        // a row here; changing or removing a row is a breaking change to that contract.
        AuthSecurityEvents.All.Select(definition => (definition.Id, definition.Name)).Should().Equal(
            (33001, "auth.sign_in.succeeded"),
            (33002, "auth.sign_in.failed"),
            (33003, "auth.sign_in.challenged"),
            (33010, "auth.throttled"),
            (33020, "auth.recovery.requested"),
            (33021, "auth.recovery.completed"),
            (33030, "auth.verification.failed"),
            (33040, "auth.step_up.succeeded"),
            (33041, "auth.step_up.failed"),
            (33050, "auth.factor.changed"),
            (33051, "auth.factor.change_failed"),
            (33060, "auth.refresh_token.reused"),
            (33070, "auth.session.revoked"),
            (33080, "auth.provider.linked"),
            (33081, "auth.provider_link.conflict"),
            (33090, "authz.denied"),
            (33100, "auth.account.status_changed"));
    }

    [Fact]
    public void Catalog_ShouldUseUniqueIdsAndDottedSnakeCaseNames()
    {
        AuthSecurityEvents.All.Select(definition => definition.Id).Should().OnlyHaveUniqueItems();
        AuthSecurityEvents.All.Select(definition => definition.Name).Should().OnlyHaveUniqueItems();
        AuthSecurityEvents.All.Should().OnlyContain(definition =>
            Regex.IsMatch(definition.Name, "^authz?(\\.[a-z_]+)+$"));
        AuthSecurityEvents.All.Should().OnlyContain(definition => definition.Id >= 33000 && definition.Id < 33900);
    }

    [Theory]
    [InlineData("SourceIp", "source_ip")]
    [InlineData("OAuthCompletion", "oauth_completion")]
    [InlineData("ApiClient", "api_client")]
    [InlineData("None", "none")]
    public void WireNames_ShouldRenderSnakeCase(string name, string expected)
    {
        AuthSecurityWireNames.ToSnakeCase(name).Should().Be(expected);
    }

    [Fact]
    public void WireNames_ShouldRenderNullAsNull()
    {
        AuthSecurityWireNames.Of((AuthSecurityMethod?)null).Should().BeNull();
        AuthSecurityWireNames.Of(AuthSecurityReason.EmailBoundToOtherProvider)
            .Should().Be("email_bound_to_other_provider");
    }

    [Fact]
    public void Event_ShouldOnlyExposeTypedOrServerGeneratedFields()
    {
        // A free-form string on the event is where a token, OTP, address, or exception message
        // would end up. Every string field must be one the emitter produces or validates itself.
        var allowedStrings = new[]
        {
            nameof(AuthSecurityEvent.SessionId),
            nameof(AuthSecurityEvent.TargetHash),
            nameof(AuthSecurityEvent.SourceIpHash),
            nameof(AuthSecurityEvent.CorrelationId),
            nameof(AuthSecurityEvent.Route),
        };

        foreach (var property in typeof(AuthSecurityEvent).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var allowed = type.IsEnum
                || type == typeof(int)
                || type == typeof(DateTimeOffset)
                || type == typeof(AuthSecurityEventDefinition)
                || (type == typeof(string) && allowedStrings.Contains(property.Name));

            allowed.Should().BeTrue($"{property.Name} ({type.Name}) could carry caller-supplied text");
        }
    }

    [Fact]
    public void Hasher_ShouldBeKeyedNormalizedAndUnrecoverable()
    {
        var hasher = AuthSecurityEventTestSupport.Hasher();
        var other = AuthSecurityEventTestSupport.Hasher("b3RoZXItdW5pdC10ZXN0LWF1dGgtc2VjdXJpdHktaGFzaC1rZXk=");
        const string email = "Ada.Lovelace@Example.com";

        var hash = hasher.HashTarget(AuthSecurityTarget.Email(email));

        hash.Should().MatchRegex("^[0-9a-f]{32}$");
        hasher.HashTarget(AuthSecurityTarget.Email(" ada.lovelace@example.COM ")).Should().Be(hash);
        other.HashTarget(AuthSecurityTarget.Email(email)).Should().NotBe(hash);
        hasher.HashTarget(AuthSecurityTarget.Username("ada.lovelace@example.com")).Should().NotBe(hash);

        var unkeyed = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("email:ada.lovelace@example.com")));
        unkeyed.Should().NotStartWith(hash!);
        hash.Should().NotContainEquivalentOf("ada");
    }

    [Fact]
    public void Hasher_ShouldNormalizeAddressesAndSkipUnknownOnes()
    {
        var hasher = AuthSecurityEventTestSupport.Hasher();

        hasher.HashSourceIp("192.0.2.10").Should().Be(hasher.HashSourceIp("::ffff:192.0.2.10"));
        hasher.HashSourceIp("not-an-ip").Should().BeNull();
        hasher.HashSourceIp(null).Should().BeNull();
        hasher.HashTarget(AuthSecurityTarget.Username("  ")).Should().BeNull();
    }

    [Fact]
    public void OptionsValidator_ShouldRequireARealKeyInProduction()
    {
        var production = new AuthSecurityEventsOptionsValidator(isNonProduction: false);
        var development = new AuthSecurityEventsOptionsValidator(isNonProduction: true);

        production.Validate(null, new AuthSecurityEventsOptions()).Failed.Should().BeTrue();
        production.Validate(null, new AuthSecurityEventsOptions
        {
            IdentifierHashKey = AuthSecurityEventsOptions.DevelopmentHashKey,
        }).Failed.Should().BeTrue();
        production.Validate(null, AuthSecurityEventTestSupport.Options()).Succeeded.Should().BeTrue();
        development.Validate(null, new AuthSecurityEventsOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void OptionsValidator_ShouldRejectMalformedOrShortKeys(string key)
    {
        var validator = new AuthSecurityEventsOptionsValidator(isNonProduction: true);

        validator.Validate(null, new AuthSecurityEventsOptions { IdentifierHashKey = key })
            .Failed.Should().BeTrue();
    }
}
