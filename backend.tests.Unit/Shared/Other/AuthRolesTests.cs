using backend.main.shared.exceptions.http;
using backend.main.shared.other;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Other;

public class AuthRolesTests
{
    [Fact]
    public void TryNormalize_ShouldRecognizeKnownRoles_CaseInsensitively()
    {
        AuthRoles.TryNormalize(" organizer ", out var organizer).Should().BeTrue();
        organizer.Should().Be(AuthRoles.Organizer);

        AuthRoles.TryNormalize("Volunteer", out var volunteer).Should().BeTrue();
        volunteer.Should().Be(AuthRoles.Volunteer);

        AuthRoles.TryNormalize("mystery", out var unknown).Should().BeFalse();
        unknown.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeOrThrow_ShouldReturnNormalizedRole_OrThrow()
    {
        AuthRoles.NormalizeOrThrow("participant").Should().Be(AuthRoles.Participant);

        var action = () => AuthRoles.NormalizeOrThrow("mystery");

        action.Should().Throw<BadRequestException>()
            .WithMessage("Role must be one of*");
    }

    [Fact]
    public void NormalizeStored_AndIsKnownRole_ShouldHandleKnownAndUnknownValues()
    {
        AuthRoles.NormalizeStored(" admin ").Should().Be(AuthRoles.Admin);
        AuthRoles.NormalizeStored(" custom-role ").Should().Be("custom-role");
        AuthRoles.NormalizeStored(null).Should().BeEmpty();
        AuthRoles.IsKnownRole("Organizer").Should().BeTrue();
        AuthRoles.IsKnownRole("custom-role").Should().BeFalse();
        AuthRoles.DefaultOAuthRole.Should().Be(AuthRoles.Participant);
        AuthRoles.SignUpRoles.Should().Equal(AuthRoles.Participant, AuthRoles.Organizer, AuthRoles.Volunteer);
        AuthRoles.AllRoles.Should().Equal(AuthRoles.Admin, AuthRoles.Organizer, AuthRoles.Participant, AuthRoles.Volunteer);
    }
}
