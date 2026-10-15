using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace backend.main.shared.attributes.validation;

public enum ContentTextViolation
{
    None,
    UnsupportedCharacters,
    ProhibitedLanguage,
}

public static class ContentTextPolicy
{
    private const string ResourceName = "ContentTextPolicy.prohibited-terms.txt";

    private static readonly Lazy<ImmutableArray<ImmutableArray<string>>> ProhibitedPhrases =
        new(LoadProhibitedPhrases);

    public static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    public static ContentTextViolation Validate(string value, bool allowLineBreaks)
    {
        if (ContainsUnsupportedCharacters(value, allowLineBreaks))
            return ContentTextViolation.UnsupportedCharacters;

        return ContainsProhibitedLanguage(value)
            ? ContentTextViolation.ProhibitedLanguage
            : ContentTextViolation.None;
    }

    public static bool ContainsProhibitedLanguage(string value)
    {
        var tokens = Tokenize(value);
        if (tokens.Length == 0)
            return false;

        foreach (var phrase in ProhibitedPhrases.Value)
        {
            if (phrase.Length > tokens.Length)
                continue;

            for (var start = 0; start <= tokens.Length - phrase.Length; start++)
            {
                var matches = true;
                for (var offset = 0; offset < phrase.Length; offset++)
                {
                    if (!string.Equals(tokens[start + offset], phrase[offset], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return true;
            }
        }

        return false;
    }

    internal static ImmutableArray<ImmutableArray<string>> GetProhibitedPhrasesForTests() =>
        ProhibitedPhrases.Value;

    private static bool ContainsUnsupportedCharacters(string value, bool allowLineBreaks)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is '<' or '>')
                return true;

            if (rune.Value == '\n')
            {
                if (!allowLineBreaks)
                    return true;

                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control
                || category == UnicodeCategory.Format && !IsAllowedFormattingRune(rune.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedFormattingRune(int value) =>
        value is 0x200C or 0x200D
        || value is >= 0xE0020 and <= 0xE007F;

    private static ImmutableArray<string> Tokenize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var tokens = ImmutableArray.CreateBuilder<string>();
        var current = new StringBuilder();

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.OtherNumber)
            {
                current.Append(rune.ToString());
                continue;
            }

            AddCurrentToken(tokens, current);
        }

        AddCurrentToken(tokens, current);
        return tokens.ToImmutable();
    }

    private static void AddCurrentToken(
        ImmutableArray<string>.Builder tokens,
        StringBuilder current)
    {
        if (current.Length == 0)
            return;

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static ImmutableArray<ImmutableArray<string>> LoadProhibitedPhrases()
    {
        using var stream = typeof(ContentTextPolicy).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);

        var phrases = ImmutableArray.CreateBuilder<ImmutableArray<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var tokens = Tokenize(trimmed);
            var canonical = string.Join(' ', tokens);
            if (tokens.Length == 0 || !string.Equals(trimmed, canonical, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Prohibited content term '{trimmed}' is not in canonical lowercase form.");
            }

            if (!seen.Add(canonical))
                throw new InvalidOperationException($"Prohibited content term '{canonical}' is duplicated.");

            phrases.Add(tokens);
        }

        if (phrases.Count == 0)
            throw new InvalidOperationException("The prohibited content term resource is empty.");

        return phrases.ToImmutable();
    }
}
