using Microsoft.Extensions.Options;

namespace backend.main.features.auth.password
{
    /// <summary>
    /// Settings for how passwords are accepted and stored, bound from <c>Auth:PasswordPolicy</c>.
    /// </summary>
    public sealed class PasswordPolicyOptions
    {
        public const string SectionName = "Auth:PasswordPolicy";

        /// <summary>NIST SP 800-63B's floor for a password used as a single factor is 8.</summary>
        public const int LowestAllowedMinLength = 8;

        /// <summary>The longest password accepted. Larger values only invite hashing-cost abuse.</summary>
        public const int HighestAllowedMaxLength = 128;

        /// <summary>
        /// Counted in Unicode scalar values after NFKC normalization. The Angular forms carry the
        /// same bounds as constants and must change together with these.
        /// </summary>
        public int MinLength { get; set; } = 15;

        public int MaxLength { get; set; } = HighestAllowedMaxLength;

        /// <summary>
        /// Whether the bundled list of common and breached passwords is loaded.
        /// </summary>
        public bool UseEmbeddedBlocklist { get; set; } = true;

        /// <summary>
        /// An optional local file of further blocked passwords, one per line, merged with the
        /// bundled list. Lines starting with <c>"# "</c> are comments. The file is read once, in
        /// process; passwords are never sent to an outside service.
        /// </summary>
        public string? BlocklistPath
        {
            get; set;
        }

        /// <summary>
        /// Words specific to this service that no password may contain, such as the product name.
        /// </summary>
        public List<string> ContextTerms { get; set; } = ["eventxperience", "event xperience"];

        public PasswordHashingOptions Hashing { get; set; } = new();
    }

    public enum PasswordHashAlgorithm
    {
        Argon2id,

        /// <summary>
        /// A rollback lever only. BCrypt reads at most 72 bytes of its input, so hashes it makes
        /// ignore the tail of a long passphrase.
        /// </summary>
        Bcrypt,
    }

    /// <summary>
    /// The parameters new hashes are made with. A stored hash made with anything else verifies as
    /// before and is replaced at the account's next successful login.
    /// </summary>
    public sealed class PasswordHashingOptions
    {
        public const int MinBcryptWorkFactor = 4;
        public const int MaxBcryptWorkFactor = 31;

        public PasswordHashAlgorithm Algorithm { get; set; } = PasswordHashAlgorithm.Argon2id;

        public Argon2Options Argon2 { get; set; } = new();

        public int BcryptWorkFactor { get; set; } = 12;
    }

    /// <summary>
    /// Argon2id cost parameters. The defaults are the OWASP baseline (19 MiB, two passes, one lane).
    /// </summary>
    public sealed class Argon2Options
    {
        public const int MinMemoryKiB = 1024;
        public const int MaxMemoryKiB = 1024 * 1024;
        public const int MaxIterations = 10;
        public const int MaxParallelism = 16;

        public int MemoryKiB { get; set; } = 19 * 1024;

        public int Iterations { get; set; } = 2;

        public int Parallelism { get; set; } = 1;
    }

    public sealed class PasswordPolicyOptionsValidator : IValidateOptions<PasswordPolicyOptions>
    {
        public ValidateOptionsResult Validate(string? name, PasswordPolicyOptions options)
        {
            var failures = new List<string>();
            var root = PasswordPolicyOptions.SectionName;

            if (options.MinLength is < PasswordPolicyOptions.LowestAllowedMinLength
                or > PasswordPolicyOptions.HighestAllowedMaxLength)
            {
                failures.Add(
                    $"{root}:MinLength must be between {PasswordPolicyOptions.LowestAllowedMinLength} and {PasswordPolicyOptions.HighestAllowedMaxLength}.");
            }

            if (options.MaxLength < options.MinLength || options.MaxLength > PasswordPolicyOptions.HighestAllowedMaxLength)
            {
                failures.Add(
                    $"{root}:MaxLength must be at least MinLength and at most {PasswordPolicyOptions.HighestAllowedMaxLength}.");
            }

            if (!string.IsNullOrWhiteSpace(options.BlocklistPath) && !File.Exists(options.BlocklistPath))
                failures.Add($"{root}:BlocklistPath points to a file that does not exist.");

            var hashing = options.Hashing;
            var section = $"{PasswordPolicyOptions.SectionName}:Hashing";

            if (!Enum.IsDefined(hashing.Algorithm))
                failures.Add($"{section}:Algorithm must be Argon2id or Bcrypt.");

            if (hashing.Argon2.MemoryKiB is < Argon2Options.MinMemoryKiB or > Argon2Options.MaxMemoryKiB)
            {
                failures.Add(
                    $"{section}:Argon2:MemoryKiB must be between {Argon2Options.MinMemoryKiB} and {Argon2Options.MaxMemoryKiB}.");
            }

            if (hashing.Argon2.Iterations is < 1 or > Argon2Options.MaxIterations)
                failures.Add($"{section}:Argon2:Iterations must be between 1 and {Argon2Options.MaxIterations}.");

            if (hashing.Argon2.Parallelism is < 1 or > Argon2Options.MaxParallelism)
                failures.Add($"{section}:Argon2:Parallelism must be between 1 and {Argon2Options.MaxParallelism}.");

            // Argon2 needs at least eight 1 KiB blocks per lane.
            if (hashing.Argon2.MemoryKiB < 8 * hashing.Argon2.Parallelism)
                failures.Add($"{section}:Argon2:MemoryKiB must be at least 8 × Parallelism.");

            if (hashing.BcryptWorkFactor is < PasswordHashingOptions.MinBcryptWorkFactor
                or > PasswordHashingOptions.MaxBcryptWorkFactor)
            {
                failures.Add(
                    $"{section}:BcryptWorkFactor must be between {PasswordHashingOptions.MinBcryptWorkFactor} and {PasswordHashingOptions.MaxBcryptWorkFactor}.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
