using System.Collections.Frozen;
using System.Text;

namespace backend.main.features.auth.events;

/// <summary>
/// Renders the event enums as the snake_case names used in logs, metric tags, and docs.
/// </summary>
public static class AuthSecurityWireNames
{
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Cache<TEnum>.Names.TryGetValue(value, out var name) ? name : "unknown";

    public static string? Of<TEnum>(TEnum? value) where TEnum : struct, Enum =>
        value is { } present ? Of(present) : null;

    /// <summary>
    /// <c>SourceIp</c> becomes <c>source_ip</c> and <c>OAuthCompletion</c> becomes
    /// <c>oauth_completion</c>: a word break is an uppercase letter after a lowercase one.
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static class Cache<TEnum> where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> Names = Enum.GetValues<TEnum>()
            .ToFrozenDictionary(value => value, value => ToSnakeCase(value.ToString()));
    }
}
