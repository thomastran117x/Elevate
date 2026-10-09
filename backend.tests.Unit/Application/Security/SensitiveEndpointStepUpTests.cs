using System.Reflection;

using backend.main.application.security;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.mfa.totp;
using backend.main.features.profile;

using FluentAssertions;

namespace backend.tests.Unit.Application.Security;

/// <summary>
/// The registry of account mutations that must carry <see cref="RequireMfaAttribute"/>. Any new
/// credential, factor, provider, session, device, or recovery-code mutation belongs here too.
/// </summary>
public class SensitiveEndpointStepUpTests
{
    public static TheoryData<Type, string> SensitiveActions => new()
    {
        { typeof(ProfileController), nameof(ProfileController.ChangeUsername) },
        { typeof(ProfileController), nameof(ProfileController.ChangePassword) },
        { typeof(ProfileController), nameof(ProfileController.RequestEmailChange) },
        { typeof(ProfileController), nameof(ProfileController.DeleteAccount) },
        { typeof(AuthMfaController), nameof(AuthMfaController.StartEnrollment) },
        { typeof(AuthMfaController), nameof(AuthMfaController.StartEnable) },
        { typeof(AuthMfaController), nameof(AuthMfaController.VerifyEnrollment) },
        { typeof(AuthMfaController), nameof(AuthMfaController.Disable) },
        { typeof(AuthMfaController), nameof(AuthMfaController.Remove) },
        { typeof(AuthTotpMfaController), nameof(AuthTotpMfaController.StartEnrollment) },
        { typeof(AuthTotpMfaController), nameof(AuthTotpMfaController.VerifyEnrollment) },
        { typeof(AuthTotpMfaController), nameof(AuthTotpMfaController.Enable) },
        { typeof(AuthTotpMfaController), nameof(AuthTotpMfaController.Disable) },
        { typeof(AuthTotpMfaController), nameof(AuthTotpMfaController.Remove) },
    };

    // Step-up itself must stay reachable without a proof, or users could never obtain one.
    public static TheoryData<string> StepUpActions => new()
    {
        nameof(AuthMfaStepUpController.GetOptions),
        nameof(AuthMfaStepUpController.Start),
        nameof(AuthMfaStepUpController.Verify),
    };

    [Theory]
    [MemberData(nameof(SensitiveActions))]
    public void SensitiveAction_ShouldRequireFreshStepUp(Type controller, string action)
    {
        IsGated(controller, action).Should().BeTrue($"{controller.Name}.{action} mutates account security");
    }

    [Theory]
    [MemberData(nameof(StepUpActions))]
    public void StepUpAction_ShouldNotRequireStepUp(string action)
    {
        IsGated(typeof(AuthMfaStepUpController), action).Should().BeFalse();
    }

    private static bool IsGated(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"{controller.Name}.{action} should exist");

        return controller.GetCustomAttribute<RequireMfaAttribute>(inherit: true) != null
            || method!.GetCustomAttribute<RequireMfaAttribute>(inherit: true) != null;
    }
}
