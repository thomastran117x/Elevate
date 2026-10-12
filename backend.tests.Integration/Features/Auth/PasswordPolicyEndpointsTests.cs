using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.token;
using backend.main.features.profile.contracts.requests;
using backend.main.shared.providers.messages;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The passphrase policy across every path that sets a password: signup, reset and authenticated
/// change, all returning the same <c>VALIDATION_ERROR</c> envelope.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PasswordPolicyEndpointsTests
{
    private const string LengthMessage = "Use between 15 and 128 characters.";
    private const string TooCommonMessage =
        "Choose a less common password. Avoid common phrases, repeated or sequential characters, "
        + "and your username, email, or the site name.";

    [Theory]
    [InlineData("Sh0rt&Strong!", LengthMessage)]
    [InlineData("p a s s w o r d 1 2 3", TooCommonMessage)]
    [InlineData("policy-user every day", TooCommonMessage)]
    [InlineData("my eventxperience login", TooCommonMessage)]
    public async Task Signup_ShouldRejectPasswordsThePolicyDisallows(string password, string expectedMessage)
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.PostJsonWithCsrfAsync("/api/auth/signup", new SignUpRequest
        {
            Email = "policy-user@example.com",
            Username = "policy-user",
            Password = password,
            Usertype = "Participant",
            Captcha = "captcha"
        });

        await ShouldBeValidationErrorAsync(response, "Password", expectedMessage);
        app.Publisher.EmailMessages.Should().NotContain(message => message.Email == "policy-user@example.com");
    }

    [Fact]
    public async Task Signup_ShouldAcceptAUnicodePassphraseWithSpaces_AndLogInWithIt()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        const string passphrase = "Ünïcödé pässphrase 🔐 wïth spaces";

        await app.SignUpAndVerifyByTokenAsync("unicode@example.com", passphrase, username: "unicode-user");
        var user = await app.FindUserByEmailAsync("unicode@example.com");
        user.Should().NotBeNull();
        user!.Password.Should().StartWith("$argon2id$");
        await app.SeedKnownDeviceAsync(user.Id, "unicode-device");

        var session = await app.LoginApiAsync("unicode-user", passphrase, trustedDeviceToken: "unicode-device");

        session.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ResetPassword_ShouldKeepTheLinkUsable_WhenTheNewPasswordIsRejected()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var user = await app.SeedUserAsync("reset-policy@example.com");

        var recovery = await app.PostJsonWithCsrfAsync("/api/auth/recovery/password", new PasswordRecoveryRequest
        {
            Username = "reset-policy",
            Captcha = "captcha"
        });
        recovery.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetEmail = app.Publisher.EmailMessages.Last(message =>
            message.Type == EmailMessageType.ResetPassword && message.Email == "reset-policy@example.com");
        var resetPath = $"/api/auth/reset-password?token={Uri.EscapeDataString(resetEmail.Token)}";

        var rejected = await app.PostJsonWithCsrfAsync(resetPath, new ResetPasswordRequest
        {
            Password = "reset-policy forever more"
        });
        await ShouldBeValidationErrorAsync(rejected, "Password", TooCommonMessage);
        (await app.QueryDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.Password).SingleAsync()))
            .Should().Be(user.Password);

        var accepted = await app.PostJsonWithCsrfAsync(resetPath, new ResetPasswordRequest
        {
            Password = "amber tidal fern 2026"
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangePassword_ShouldRejectAnAccountDerivedPassword()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "change-policy@example.com", transport: SessionTransportResolver.ApiValue);
        await app.CompleteSessionMfaByEmailAsync("change-policy@example.com", session.AccessToken);

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/profile/change-password",
            new ChangePasswordAuthenticatedRequest
            {
                CurrentPassword = AuthApiTestApp.DefaultPassword,
                NewPassword = "change-policy all year"
            },
            session.AccessToken);

        await ShouldBeValidationErrorAsync(response, "NewPassword", TooCommonMessage);
    }

    [Fact]
    public async Task ChangePassword_ShouldEndEveryExistingSession()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await app.SignUpAndVerifyByTokenAsync(
            "change-sessions@example.com", transport: SessionTransportResolver.ApiValue);
        await app.CompleteSessionMfaByEmailAsync("change-sessions@example.com", session.AccessToken);

        var change = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/profile/change-password",
            new ChangePasswordAuthenticatedRequest
            {
                CurrentPassword = AuthApiTestApp.DefaultPassword,
                NewPassword = "amber tidal fern 2026"
            },
            session.AccessToken);
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        var me = await app.GetWithBearerAsync("/api/auth/me", session.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refresh = await app.Client.PostAsJsonAsync("/api/auth/api/refresh", new RefreshTokenRequest
        {
            RefreshToken = session.RefreshToken,
            SessionBindingToken = session.SessionBindingToken
        });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task ShouldBeValidationErrorAsync(
        HttpResponseMessage response, string field, string expectedMessage)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        using var document = JsonDocument.Parse(body);
        var error = document.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");

        var details = error.GetProperty("details").EnumerateObject()
            .Single(property => string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase));
        details.Value.EnumerateArray().Select(message => message.GetString())
            .Should().Equal(expectedMessage);
    }
}
