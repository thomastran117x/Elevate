using backend.main.features.auth.password;

using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Features.Auth.Password;

public class PasswordPolicyTests
{
    private static readonly PasswordPolicyContext Ada = new("ada_lovelace", "ada.lovelace@analytical.org");
    private static readonly PasswordPolicyContext NoAccount = new(null, null);

    [Theory]
    [InlineData("lantern orchard velvet compass")]
    [InlineData("Ünïcödé pässphrase wïth spaces")]
    [InlineData("emoji 🔐🦄 and CJK 密码短语 ok")]
    [InlineData("kX9#mQ2$vL7!pR4&")]
    [InlineData("correct-horse-battery-staple")]
    public void Evaluate_ShouldAllowPassphrasesAndGeneratedValues(string password)
    {
        TestPasswordPolicies.Default().Evaluate(password, Ada).Should().Be(PasswordPolicyViolation.None);
    }

    [Theory]
    [InlineData(14, PasswordPolicyViolation.TooShort)]
    [InlineData(129, PasswordPolicyViolation.TooLong)]
    public void Evaluate_ShouldRejectLengthsOutsideTheRange(int length, PasswordPolicyViolation expected)
    {
        var password = string.Concat(Enumerable.Range(0, length).Select(i => "lantern orchard "[i % 16]));

        TestPasswordPolicies.Default().Evaluate(password, NoAccount).Should().Be(expected);
    }

    [Theory]
    [InlineData("P a s s w o r d !!")]
    [InlineData("p-a-s-s-w-o-r-d-#")]
    [InlineData("PASSWORD________")]
    [InlineData("letmein letmein!!")]
    [InlineData("1234iloveyou5678")]
    public void Evaluate_ShouldRejectBlocklistedValues_AfterNormalization(string password)
    {
        TestPasswordPolicies.Default().Evaluate(password, NoAccount).Should().Be(PasswordPolicyViolation.Common);
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abababababababab")]
    [InlineData("abcabcabcabcabcabc")]
    [InlineData("monkeymonkeymonkey")]
    [InlineData("monkeymonkeymonkey2024")]
    [InlineData("123456789012345")]
    [InlineData("abcdefghijklmnop")]
    [InlineData("zyxwvutsrqponmlk")]
    [InlineData("qwertyuiopasdfgh")]
    [InlineData("1qaz2wsx3edc4rfv")]
    [InlineData("ab ab ab ab ab ab")]
    public void Evaluate_ShouldRejectRepetitiveSequentialAndKeyboardPatterns(string password)
    {
        TestPasswordPolicies.Default().Evaluate(password, NoAccount).Should().Be(PasswordPolicyViolation.Common);
    }

    [Theory]
    [InlineData("ada_lovelace forever!")]
    [InlineData("I am AdaLovelace 1815")]
    [InlineData("lovelace was first")]
    [InlineData("analytical engine rules")]
    [InlineData("my eventxperience pass")]
    [InlineData("Event Xperience is great")]
    public void Evaluate_ShouldRejectAccountDerivedAndProductNamePasswords(string password)
    {
        TestPasswordPolicies.Default().Evaluate(password, Ada).Should().Be(PasswordPolicyViolation.Personal);
    }

    [Fact]
    public void Evaluate_ShouldIgnoreShortAccountFragments()
    {
        var context = new PasswordPolicyContext("bo", "al@x.io");

        TestPasswordPolicies.Default().Evaluate("bold alpine boxes", context).Should().Be(PasswordPolicyViolation.None);
    }

    [Fact]
    public void Evaluate_ShouldNotTreatTheEmailTldAsPersonal()
    {
        var context = new PasswordPolicyContext(null, "someone@mail.network");

        TestPasswordPolicies.Default().Evaluate("my network of friends", context).Should().Be(PasswordPolicyViolation.None);
        TestPasswordPolicies.Default().Evaluate("my mail is private", context).Should().Be(PasswordPolicyViolation.Personal);
    }

    [Fact]
    public void Evaluate_ShouldHonourConfiguredContextTerms()
    {
        var options = new PasswordPolicyOptions();
        options.ContextTerms.Add("harbour striders");
        var policy = new PasswordPolicy(Options.Create(options), new TestPasswordPolicies.InMemoryPasswordBlocklist([]));

        policy.Evaluate("go harbourstriders go", NoAccount).Should().Be(PasswordPolicyViolation.Personal);
    }

    [Fact]
    public void EnsureAllowed_ShouldThrowOneLengthMessage_ForTheField()
    {
        var act = () => TestPasswordPolicies.Default().EnsureAllowed("too short", NoAccount, "NewPassword");

        var error = act.Should().Throw<PasswordPolicyException>().Which;
        error.StatusCode.Should().Be(400);
        error.ErrorCode.Should().Be("VALIDATION_ERROR");
        error.Details.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["NewPassword"] = ["Use between 15 and 128 characters."],
        });
    }

    [Fact]
    public void EnsureAllowed_ShouldUseTheSameMessage_ForEveryContentRule()
    {
        var policy = TestPasswordPolicies.Default();

        var common = Capture(() => policy.EnsureAllowed("P a s s w o r d !!", Ada, "Password"));
        var personal = Capture(() => policy.EnsureAllowed("ada_lovelace forever!", Ada, "Password"));

        common.Should().Be(PasswordPolicyMessages.TooCommon);
        personal.Should().Be(PasswordPolicyMessages.TooCommon);
        common.Should().NotContainAny("blocklist", "breach", "list");
    }

    [Fact]
    public void EnsureAllowed_ShouldPass_ForAnAcceptablePassword()
    {
        var act = () => TestPasswordPolicies.Default().EnsureAllowed("lantern orchard velvet compass", Ada, "Password");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("abcabc", true)]
    [InlineData("abcab", false)]
    [InlineData("a", false)]
    public void IsRepetition_ShouldDetectWholeUnitRepeats(string value, bool expected)
    {
        PasswordPolicy.IsRepetition(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("7890123", true)]
    [InlineData("3210987", true)]
    [InlineData("abd", false)]
    [InlineData("abce", false)]
    public void IsSequence_ShouldFollowRunsIncludingTheDigitWrap(string value, bool expected)
    {
        PasswordPolicy.IsSequence(value).Should().Be(expected);
    }

    private static string Capture(Action act)
    {
        var details = act.Should().Throw<PasswordPolicyException>().Which.Details
            .Should().BeOfType<Dictionary<string, string[]>>().Subject;
        return details.Values.Single().Single();
    }
}
