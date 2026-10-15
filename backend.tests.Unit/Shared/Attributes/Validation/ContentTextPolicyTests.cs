using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using backend.main.shared.attributes.validation;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Attributes.Validation;

public class ContentTextPolicyTests
{
    [Fact]
    public void Normalize_ShouldTrimAndCanonicalizeLineEndingsIdempotently()
    {
        const string input = "  Café\r\nfamily 👨‍👩‍👧‍👦  ";

        var once = ContentTextPolicy.Normalize(input);
        var twice = ContentTextPolicy.Normalize(once);

        once.Should().Be("Café\nfamily 👨‍👩‍👧‍👦");
        twice.Should().Be(once);
        ContentTextPolicy.Validate(once, allowLineBreaks: true)
            .Should().Be(ContentTextViolation.None);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("hello\tworld")]
    [InlineData("hello\u0000world")]
    [InlineData("hello\u202Eworld")]
    [InlineData("hello\u200Bworld")]
    [InlineData("hello\u00ADworld")]
    public void Validate_ShouldRejectMarkupControlsAndSpoofingCharacters(string input)
    {
        ContentTextPolicy.Validate(input, allowLineBreaks: true)
            .Should().Be(ContentTextViolation.UnsupportedCharacters);
    }

    [Fact]
    public void Validate_ShouldAllowLineBreaksOnlyForMultilineText()
    {
        ContentTextPolicy.Validate("first\nsecond", allowLineBreaks: true)
            .Should().Be(ContentTextViolation.None);
        ContentTextPolicy.Validate("first\nsecond", allowLineBreaks: false)
            .Should().Be(ContentTextViolation.UnsupportedCharacters);
    }

    [Fact]
    public void Validate_ShouldPreserveEmojiFormattingSequences()
    {
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        const string englandFlag = "\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F";

        ContentTextPolicy.Validate(family + englandFlag, allowLineBreaks: false)
            .Should().Be(ContentTextViolation.None);
    }

    [Theory]
    [InlineData("This contains FUCK.")]
    [InlineData("ＦＵＣＫ")]
    [InlineData("a porn review")]
    [InlineData("You should KILL YOURSELF!")]
    public void Validate_ShouldRejectWholeProhibitedWordsAfterCompatibilityNormalization(string input)
    {
        ContentTextPolicy.Validate(input, allowLineBreaks: true)
            .Should().Be(ContentTextViolation.ProhibitedLanguage);
    }

    [Theory]
    [InlineData("classical analysis assistant")]
    [InlineData("shellfuckword")]
    [InlineData("skill yourself")]
    [InlineData("f4ck")]
    public void Validate_ShouldNotUseSubstringOrEvasionMatching(string input)
    {
        ContentTextPolicy.Validate(input, allowLineBreaks: true)
            .Should().Be(ContentTextViolation.None);
    }

    [Fact]
    public void ProhibitedTermResource_ShouldContainCanonicalUniqueRecognizedEntries()
    {
        var phrases = ContentTextPolicy.GetProhibitedPhrasesForTests();
        phrases.Should().NotBeEmpty();

        var canonical = phrases.Select(phrase => string.Join(' ', phrase)).ToList();
        canonical.Should().OnlyHaveUniqueItems();
        canonical.Should().OnlyContain(value => value == value.Trim().ToLowerInvariant());
        canonical.Should().OnlyContain(value => ContentTextPolicy.ContainsProhibitedLanguage(value));
    }

    [Fact]
    public void JsonMetadata_ShouldNormalizeOnlyHumanTextBeforeValidation()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        RequestTextJsonMetadata.Configure(options);

        var request = JsonSerializer.Deserialize<TestRequest>(
            """
            {
              "title": "  hello  ",
              "body": "  first\r\nsecond  ",
              "tags": ["  one  ", null, " two "],
              "structured": "  value  ",
              "secret": "  password  "
            }
            """,
            options)!;

        request.Title.Should().Be("hello");
        request.Body.Should().Be("first\nsecond");
        request.Tags.Should().Equal("one", null, "two");
        request.Structured.Should().Be("  value  ");
        request.Secret.Should().Be("  password  ");

        Validator.TryValidateObject(request, new ValidationContext(request), [], true)
            .Should().BeTrue("length validation should see the normalized title");
    }

    [Fact]
    public void RequestTextAttribute_ShouldReturnTheMemberNameWithoutEchoingInput()
    {
        var request = new TestRequest { Title = "<x>" };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, true)
            .Should().BeFalse();

        results.Should().ContainSingle();
        results[0].MemberNames.Should().Equal(nameof(TestRequest.Title));
        results[0].ErrorMessage.Should().Be("The text contains markup or unsupported characters.");
        results[0].ErrorMessage.Should().NotContain("unsafe");
    }

    private sealed class TestRequest
    {
        [RequestText(RequestTextKind.HumanSingleLine)]
        [StringLength(5)]
        public string Title { get; set; } = string.Empty;

        [RequestText(RequestTextKind.HumanMultiline)]
        public string Body { get; set; } = string.Empty;

        [RequestText(RequestTextKind.HumanSingleLine)]
        public List<string?> Tags { get; set; } = [];

        [RequestText(RequestTextKind.Structured)]
        public string Structured { get; set; } = string.Empty;

        [RequestText(RequestTextKind.Sensitive)]
        public string Secret { get; set; } = string.Empty;
    }
}
