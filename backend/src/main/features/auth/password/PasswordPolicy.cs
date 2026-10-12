using System.Text;

using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.password
{
    public enum PasswordPolicyViolation
    {
        None,
        TooShort,
        TooLong,

        /// <summary>Blocklisted, repetitive, sequential or a keyboard walk.</summary>
        Common,

        /// <summary>Contains the account's username, email or a service-specific term.</summary>
        Personal,
    }

    /// <summary>The account details a new password must not be derived from.</summary>
    public sealed record PasswordPolicyContext(string? Username, string? Email);

    /// <summary>
    /// Decides whether a new password may be set, along the lines of NIST SP 800-63B: a length
    /// range and a check against known-bad and account-derived values, with no composition rules.
    /// Applies only when a password is chosen; existing passwords keep working at login.
    /// </summary>
    public interface IPasswordPolicy
    {
        PasswordPolicyViolation Evaluate(string password, PasswordPolicyContext context);

        /// <summary>
        /// Throws <see cref="PasswordPolicyException"/> against <paramref name="field"/> when the
        /// password is not allowed.
        /// </summary>
        void EnsureAllowed(string password, PasswordPolicyContext context, string field);
    }

    public static class PasswordPolicyMessages
    {
        public static string Length(int minLength, int maxLength) =>
            $"Use between {minLength} and {maxLength} characters.";

        /// <summary>
        /// One message for every content rule, so a rejection says what to do without revealing
        /// which list or rule matched.
        /// </summary>
        public const string TooCommon =
            "Choose a less common password. Avoid common phrases, repeated or sequential characters, "
            + "and your username, email, or the site name.";
    }

    public sealed class PasswordPolicy : IPasswordPolicy
    {
        private const int MinContextTermLength = 4;
        private const int MinContextTokenLength = 5;
        private const int MinDistinctCharacters = 4;
        private const int MinSequenceLength = 4;

        private static readonly string[] KeyboardWalks =
        [
            "1234567890qwertyuiopasdfghjklzxcvbnm",
            "qwertzuiopasdfghjklyxcvbnm",
            "azertyuiopqsdfghjklmwxcvbn",
            "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p",
            "zaq1xsw2cde3vfr4bgt5nhy6mju7ki8lo9p0",
        ];

        private readonly PasswordPolicyOptions _options;
        private readonly IPasswordBlocklist _blocklist;
        private readonly string[] _contextTerms;

        public PasswordPolicy(IOptions<PasswordPolicyOptions> options, IPasswordBlocklist blocklist)
        {
            _options = options.Value;
            _blocklist = blocklist;
            _contextTerms = _options.ContextTerms
                .Select(PasswordText.Skeleton)
                .Where(term => term.Length >= MinContextTermLength)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public PasswordPolicyViolation Evaluate(string password, PasswordPolicyContext context)
        {
            var length = PasswordText.Length(password);
            if (length < _options.MinLength)
                return PasswordPolicyViolation.TooShort;
            if (length > _options.MaxLength)
                return PasswordPolicyViolation.TooLong;

            var folded = PasswordText.Normalize(password).ToLowerInvariant();
            if (CountDistinctRunes(folded) < MinDistinctCharacters)
                return PasswordPolicyViolation.Common;

            var skeleton = PasswordText.Skeleton(password);
            // "passwordpassword2024" and "2024passwordpassword" reduce to the same core.
            var core = skeleton.Trim("0123456789".ToCharArray());

            if (IsCommon(skeleton) || (core.Length > 0 && core != skeleton && IsCommon(core)))
                return PasswordPolicyViolation.Common;

            if (ContextTerms(context).Any(term => skeleton.Contains(term, StringComparison.Ordinal)))
                return PasswordPolicyViolation.Personal;

            return PasswordPolicyViolation.None;
        }

        public void EnsureAllowed(string password, PasswordPolicyContext context, string field)
        {
            var violation = Evaluate(password, context);
            if (violation == PasswordPolicyViolation.None)
                return;

            // The reason only, for watching rejection rates after rollout. Never the value.
            Logger.Info($"[PasswordPolicy] Rejected a new password: {violation}");

            throw new PasswordPolicyException(
                field,
                violation is PasswordPolicyViolation.TooShort or PasswordPolicyViolation.TooLong
                    ? PasswordPolicyMessages.Length(_options.MinLength, _options.MaxLength)
                    : PasswordPolicyMessages.TooCommon);
        }

        private bool IsCommon(string skeleton) =>
            skeleton.Length > 0
            && (_blocklist.Contains(skeleton)
                || IsRepetition(skeleton)
                || IsSequence(skeleton)
                || IsKeyboardWalk(skeleton));

        private IEnumerable<string> ContextTerms(PasswordPolicyContext context)
        {
            foreach (var term in _contextTerms)
                yield return term;

            foreach (var term in AccountTerms(context))
                yield return term;
        }

        /// <summary>
        /// The whole username and email local part, plus their longer separator-delimited pieces
        /// (<c>"ada.lovelace"</c> gives <c>"adalovelace"</c> and <c>"lovelace"</c>), and the email's
        /// domain labels other than the TLD.
        /// </summary>
        private static IEnumerable<string> AccountTerms(PasswordPolicyContext context)
        {
            var sources = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.Username))
                sources.Add(context.Username);

            string? domain = null;
            if (!string.IsNullOrWhiteSpace(context.Email))
            {
                var at = context.Email.LastIndexOf('@');
                sources.Add(at > 0 ? context.Email[..at] : context.Email);
                domain = at > 0 ? context.Email[(at + 1)..] : null;
            }

            foreach (var source in sources)
            {
                var whole = PasswordText.Skeleton(source);
                if (whole.Length >= MinContextTermLength)
                    yield return whole;

                foreach (var token in SplitOnNonAlphanumerics(source))
                {
                    var piece = PasswordText.Skeleton(token);
                    if (piece.Length >= MinContextTokenLength && piece != whole && piece.Any(char.IsLetter))
                        yield return piece;
                }
            }

            if (domain is not null)
            {
                var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
                foreach (var label in labels.Take(Math.Max(labels.Length - 1, 0)))
                {
                    var piece = PasswordText.Skeleton(label);
                    if (piece.Length >= MinContextTermLength)
                        yield return piece;
                }
            }
        }

        private static IEnumerable<string> SplitOnNonAlphanumerics(string value)
        {
            var current = new StringBuilder();
            foreach (var rune in value.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune))
                {
                    current.Append(rune.ToString());
                }
                else if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }

            if (current.Length > 0)
                yield return current.ToString();
        }

        private static int CountDistinctRunes(string value)
        {
            var distinct = new HashSet<Rune>();
            foreach (var rune in value.EnumerateRunes())
                distinct.Add(rune);

            return distinct.Count;
        }

        /// <summary>A shorter unit repeated end to end: "abcabcabc", "passwordpassword".</summary>
        internal static bool IsRepetition(string value) =>
            value.Length >= 2 && (value + value).IndexOf(value, 1, StringComparison.Ordinal) < value.Length;

        /// <summary>
        /// A straight run up or down the alphabet or the digits, with 9 and 0 adjacent:
        /// "abcdefghijklmnop", "123456789012345", "987654321".
        /// </summary>
        internal static bool IsSequence(string value)
        {
            if (value.Length < MinSequenceLength)
                return false;

            return IsRun(value, ascending: true) || IsRun(value, ascending: false);
        }

        private static bool IsRun(string value, bool ascending)
        {
            for (var i = 1; i < value.Length; i++)
            {
                var previous = value[i - 1];
                var next = value[i];
                var step = ascending ? next - previous : previous - next;
                var wraps = ascending
                    ? previous == '9' && next == '0'
                    : previous == '0' && next == '9';
                if (step != 1 && !wraps)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// A stretch of a common keyboard row or column walk, read either way:
        /// "qwertyuiopasdfg", "1qaz2wsx3edc4rfv".
        /// </summary>
        internal static bool IsKeyboardWalk(string value)
        {
            if (value.Length < MinSequenceLength)
                return false;

            var reversed = new string(value.Reverse().ToArray());
            return KeyboardWalks.Any(walk =>
                walk.Contains(value, StringComparison.Ordinal)
                || walk.Contains(reversed, StringComparison.Ordinal));
        }
    }
}
